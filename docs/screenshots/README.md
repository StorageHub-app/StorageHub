# Screenshots

The images beside this file are the ones the README shows. They are rendered headlessly by the
desktop's own tests rather than captured from anyone's real setup, so they hold no saved
connections, hosts or files beyond what the tests script: the terminal's session and its
`build-box` host are a fixture, and the only real data is the drive list of the machine that ran
them.

To remake them, set `STORAGEHUB_SHOT_DIR` to an empty folder and run the shot tests, for example:

```powershell
$env:STORAGEHUB_SHOT_DIR = "$env:TEMP\storagehub-shots"
dotnet test tests/StorageHub.Desktop.Tests --filter "FullyQualifiedName~ShellScalingTests|FullyQualifiedName~SettingsWindowTests|FullyQualifiedName~WorkspaceLayoutViewTests"
```

Run them with no StorageHub agent answering for the account, or on Linux with `XDG_RUNTIME_DIR`
pointed at an empty directory. The workspace samples ask whatever agent answers for its saved
connections, and a picture should not show those. They are taken at 125% scale, which is what
the application was laid out against.

| File | Test output | Shows |
| --- | --- | --- |
| `welcome.png` | the installed .deb under WSLg, grabbed with `import -window` | Welcome, dark, on Linux, agent connected |
| `welcome-linux-light.png` | `shell-linux-light-1.25x.png` | Welcome, light, on Linux |
| `workspace.png` | `workspace-terminal-dark.png` | A two-pane workspace: This PC beside an SSH terminal |
| `settings-appearance.png` | `settings-appearance-light.png` | Settings, Appearance page, light |

Name each one after what it shows. A file named for the tool that captured it tells a reader
nothing, and the README's alt text has to be written from the picture either way.
