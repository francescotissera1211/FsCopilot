namespace FsCopilot.Accessibility;

using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using ReactiveUI;
using Serilog;

/// <summary>
/// Speaks short messages through the screen reader: join results, peers arriving and leaving,
/// who has the controls, errors. A sighted pilot sees these change on screen; a blind pilot
/// would otherwise have to go looking for them. Every window carries a <see cref="LiveAnnouncer"/>
/// that turns each message into a UI Automation live-region change.
/// </summary>
public static class Announcer
{
    private static readonly Subject<string> MessagesSubject = new();

    public static IObservable<string> Messages => MessagesSubject;

    public static void Say(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Log.Debug("[Announce] {Message}", message);
        MessagesSubject.OnNext(message);
    }
}

/// <summary>
/// Speaks <see cref="Announcer"/> messages for the window it sits in. Each window carries one.
/// <para>
/// Avalonia 11.3 does not raise UI Automation live-region events on Windows (its UIA layer only
/// raises focus changes), so a live region would stay silent. This raises the UIA notification
/// event instead - the mechanism WinUI's RaiseNotificationEvent uses, which NVDA, JAWS and
/// Narrator speak - with the window as its source. The control itself is kept out of the
/// automation tree, so the last message is not read a second time while exploring the window.
/// </para>
/// Only the active window speaks, so a message is not heard twice while two windows are open;
/// when none is active the newest window speaks.
/// </summary>
public sealed class LiveAnnouncer : TextBlock
{
    private static readonly List<LiveAnnouncer> Attached = [];
    private IDisposable? _subscription;

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public LiveAnnouncer()
    {
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Opacity = 0;
        IsHitTestVisible = false;
        Height = 1;
        Width = 1;
        FontSize = 1;
        Focusable = false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        lock (Attached) Attached.Add(this);
        _subscription = Announcer.Messages
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(Speak);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        lock (Attached) Attached.Remove(this);
        _subscription?.Dispose();
        _subscription = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void Speak(string message)
    {
        Text = message;

        if (TopLevel.GetTopLevel(this) is not Window window) return;
        bool speaker;
        lock (Attached)
        {
            var active = Attached.FirstOrDefault(a => TopLevel.GetTopLevel(a) is Window { IsActive: true });
            speaker = active is not null ? ReferenceEquals(active, this) : ReferenceEquals(Attached[^1], this);
        }
        if (!speaker) return;

        UiaNotification.Raise(window, message);
    }
}

/// <summary>
/// UiaRaiseNotificationEvent (Windows 10 1709 and later), raised on Avalonia's own UIA provider
/// for the window. A provider made with UiaHostProviderFromHwnd is accepted but its events reach
/// no client, so the window's real provider is fetched: Avalonia's AutomationNode is internal,
/// but its static GetOrCreate is public and the class is a source-generated COM class, so any
/// StrategyBasedComWrappers can hand out its IRawElementProviderSimple.
/// </summary>
internal static class UiaNotification
{
    private const int NotificationKindOther = 4;
    private const int NotificationProcessingImportantAll = 0;
    private const string ActivityId = "FsCopilot.Announcement";
    private static readonly Guid IidRawElementProviderSimple = new("d6dd68d1-86fd-4332-8666-9abedea2d24c");
    private static readonly StrategyBasedComWrappers Wrappers = new();
    private static bool _unavailable;
    private static MethodInfo? _getOrCreate;

    [DynamicDependency("GetOrCreate", "Avalonia.Win32.Automation.AutomationNode", "Avalonia.Win32.Automation")]
    public static void Raise(Window window, string message)
    {
        if (_unavailable || !OperatingSystem.IsWindows()) return;
        IntPtr unknown = IntPtr.Zero, provider = IntPtr.Zero;
        try
        {
            _getOrCreate ??= Type.GetType("Avalonia.Win32.Automation.AutomationNode, Avalonia.Win32.Automation")
                ?.GetMethod("GetOrCreate", BindingFlags.Public | BindingFlags.Static);
            var node = _getOrCreate?.Invoke(null, [ControlAutomationPeer.CreatePeerForElement(window)]);
            if (node is not null)
            {
                unknown = Wrappers.GetOrCreateComInterfaceForObject(node, CreateComInterfaceFlags.None);
                var iid = IidRawElementProviderSimple;
                if (Marshal.QueryInterface(unknown, ref iid, out provider) != 0) provider = IntPtr.Zero;
            }

            // Fallback: the host provider. Accepted by Windows, though not every client hears it.
            if (provider == IntPtr.Zero)
            {
                var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd == IntPtr.Zero || UiaHostProviderFromHwnd(hwnd, out provider) != 0) return;
            }

            var hr = UiaRaiseNotificationEvent(provider, NotificationKindOther, NotificationProcessingImportantAll,
                message, ActivityId);
            if (hr != 0) Log.Debug("[Announce] UiaRaiseNotificationEvent returned 0x{Hr:X8}", hr);
        }
        catch (EntryPointNotFoundException)
        {
            _unavailable = true;   // Windows older than 10 1709
            Log.Information("[Announce] UIA notifications are not available on this Windows version");
        }
        catch (Exception e)
        {
            Log.Debug(e, "[Announce] UIA notification failed");
        }
        finally
        {
            if (provider != IntPtr.Zero) Marshal.Release(provider);
            if (unknown != IntPtr.Zero) Marshal.Release(unknown);
        }
    }

    [DllImport("UIAutomationCore.dll", ExactSpelling = true)]
    private static extern int UiaHostProviderFromHwnd(IntPtr hwnd, out IntPtr provider);

    [DllImport("UIAutomationCore.dll", ExactSpelling = true)]
    private static extern int UiaRaiseNotificationEvent(IntPtr provider, int notificationKind, int notificationProcessing,
        [MarshalAs(UnmanagedType.BStr)] string displayString, [MarshalAs(UnmanagedType.BStr)] string activityId);
}
