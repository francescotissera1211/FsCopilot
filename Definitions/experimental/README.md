# Experimental profiles

Profiles here are not loaded: FS Copilot only reads aircraft profiles from the top level of
`Definitions/`. To try one, copy it over the top-level file of the same name.

- `microsoft_pa28_236_dakota.yaml`: LocatedInSpace's rework of the PA-28-236 Dakota autopilot sync
  (A: simvars and K: events instead of B: events). Its author marked it as probably not working
  ("attempt at syncing them with A... dont think it really works"). The loaded Dakota profile is the
  one published on the FS Copilot profile server, which matches LocatedInSpace's first version.
- `PMDG 737-800.yaml`: degroat-c's conversion of the YourControls PMDG 737NG definitions
  ([degroat-c/pmdg737-fscopilot](https://github.com/degroat-c/pmdg737-fscopilot), January 2026). Its
  header says some behaviours may need manual adjustment. The loaded 737-800 profile is the newer
  hand-built V2.1 by Hawks1326 and Av8rChris from the profile server.
