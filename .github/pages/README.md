# GitHub Pages WebGL fallback

`RuleforgeTD/` contains the latest locally verified four-scene WebGL build.
The Pages workflow uses this payload only when Unity license credentials are
not configured in GitHub Actions. When credentials are available, CI replaces
the staged fallback with a fresh build from
`MainMenuSceneBuilder.BuildWebGLFromCommandLine`.

The checked-in payload was built with Unity `2022.3.62f2` and contains:

- `MainMenu`
- `Stage01`
- `Stage02`
- `Stage03`

Before publication it passed the full EditMode suite, the music PlayMode
suite, deterministic balance checks, and HTTP verification of every required
WebGL payload file.
