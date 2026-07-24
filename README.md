# QuizLock

A Windows kiosk-lockdown app for Microsoft Forms: paste in a form link, it
goes fullscreen, blocks the usual ways to escape (Win key, Alt+Tab, Alt+F4,
Ctrl+Esc, taskbar), and blocks navigation to known AI-assistant sites
(ChatGPT, Claude, Gemini, Copilot, Perplexity, etc.) while it's active.

## What this is (and isn't)

This is a **kiosk browser**, not a Windows-account lock screen. It cannot be
made unbreakable, and that's by design - see "Safety notes" below.

## Requirements

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (to build)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
  (already installed on most up-to-date Windows 10/11 machines; the app will
  tell you if it's missing)
- Visual Studio 2022 (recommended) or the `dotnet` CLI

## Build

```
cd QuizLock
dotnet restore
dotnet build -c Release
```

Or open `QuizLock.csproj` in Visual Studio and press Build/Run.

The app requests admin rights (see `app.manifest`) because the keyboard hook
and taskbar control are more reliable elevated.

## Using it

1. Run `QuizLock.exe`.
2. Paste your Microsoft Forms link (e.g. `https://forms.office.com/r/...`).
3. Set an unlock password - **you need this**, so don't forget it.
4. **Strict mode is checked by default** - restricts navigation to only the
   form's own site (and Microsoft sign-in redirects, since Forms often
   requires signing in). Uncheck it only if you want other sites reachable.
5. Optionally set an auto-unlock time limit.
6. Click **Start Lockdown**. This immediately:
   - Goes fullscreen with no window border/taskbar
   - Hides the Windows taskbar
   - Blocks the Windows key, Alt+Tab, Alt+F4, and Ctrl+Esc (so nothing else
     can be switched to or opened)
   - Disables right-click and devtools inside the form view
   - Restricts navigation to only the form's own domain (strict mode) and
     always blocks known AI-assistant sites regardless of strict mode
7. When done (or to bail early), press **Ctrl+Alt+Shift+U**, enter the
   password, and it unlocks back to the setup screen.

### A note on Microsoft Forms specifically

The app automatically appends `&embed=true` to `forms.office.com` /
`forms.microsoft.com` links, which tells Forms to hide its own header/branding
so it looks cleaner in a fullscreen kiosk view - you don't need to add this
yourself.

Microsoft Forms officially supports being embedded in an iframe (it's one of
their own sharing options), so it should load reliably here, unlike some
third-party quiz sites that block embedding outright.

## Safety notes - read this

Several layers ensure this can never actually strand you on your own laptop:

- **Ctrl+Alt+Delete is untouched.** No user-mode app (this one included) can
  intercept it - Windows handles it below the application layer. That's your
  permanent backstop: it always gets you to the secure screen with Task
  Manager, sign-out, etc., no matter what bugs exist in this code.
- **You set the unlock password yourself**, each session - there's no hidden
  master password and nothing is transmitted anywhere.
- **The hotkey (Ctrl+Alt+Shift+U) is registered independently of the keyboard
  hook**, so it keeps working even while other keys are being blocked.
- **Fail-safes on exit/crash**: taskbar visibility and the keyboard hook are
  restored in `FormClosing`, `ProcessExit`, and `UnhandledException` handlers.
- This only affects the app's own window and hooks - it does **not** touch
  Windows login, BitLocker, the registry, or anything at boot. Worst case if
  something goes wrong, a restart clears everything.

## Customizing the AI blocklist

Edit the `AiBlocklist` array in `MainForm.cs` to add or remove domains.

## Files

- `MainForm.cs` - setup screen + fullscreen lockdown + WebView2 + blocklist logic
- `KeyboardHook.cs` - low-level keyboard hook (Win key / Alt+Tab / Alt+F4 / Ctrl+Esc)
- `UnlockPromptForm.cs` - small password dialog shown on the unlock hotkey
- `NativeMethods.cs` - Win32 P/Invoke declarations
- `Program.cs` - entry point
