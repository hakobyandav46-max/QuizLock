# QuizLock

A Windows kiosk-lockdown app: paste in a quiz link, it goes fullscreen, blocks
the usual ways to escape (Win key, Alt+Tab, Alt+F4, Ctrl+Esc, taskbar), and
blocks navigation to known AI-assistant sites (ChatGPT, Claude, Gemini,
Copilot, Perplexity, etc.) while it's active.

## What this is (and isn't)

This is a **kiosk browser**, not a Windows-account lock screen. It cannot be
made unbreakable, and that's by design — see "Safety notes" below.

## Requirements

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (to build)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
  (already installed on most up-to-date Windows 10/11 machines; the app will
  tell you if it's missing)
- Visual Studio 2022 (recommended) or the `dotnet` CLI

## Build

Since this environment can't reach NuGet, build it on your own Windows machine:

```
cd QuizLock
dotnet restore
dotnet build -c Release
```

Or just open `QuizLock.csproj` in Visual Studio and press Build/Run.

The app requests admin rights (see `app.manifest`) because the keyboard hook
and taskbar control are more reliable elevated. If you'd rather not run it as
admin, delete the `<ApplicationManifest>` line from the `.csproj` and the
`app.manifest` file — it will still work, just slightly less robustly on some
setups.

## Using it

1. Run `QuizLock.exe`.
2. Paste the quiz URL (e.g. your QuizMaker link, `https://take.quiz-maker.com/...`).
3. Set an unlock password — **you need this**, so don't forget it.
4. **Strict mode is checked by default** — this restricts navigation to only
   the quiz's own site (and common SSO login redirects like Google/Microsoft
   sign-in). Uncheck it only if you want other sites reachable too.
5. Optionally set an auto-unlock time limit.
6. Click **Start Lockdown**. This immediately:
   - Goes fullscreen with no window border/taskbar
   - Hides the Windows taskbar
   - Blocks the Windows key, Alt+Tab, Alt+F4, and Ctrl+Esc (so nothing else
     can be switched to or opened)
   - Disables right-click and devtools inside the quiz view
   - Restricts navigation to only the quiz's own domain (strict mode) and
     always blocks known AI-assistant sites regardless of strict mode
7. When you're done (or need to bail early), press **Ctrl+Alt+Shift+U**,
   enter your password, and it unlocks back to the setup screen.
8. On unlock, it automatically appends a row to the Excel file you set in
   step 6: name, whatever score it auto-detected (see caveat below - may be
   blank if it couldn't find one), quiz link, and timestamp. This happens
   with no prompt - it always saves.

### About the auto-detected score

The app scans the visible text of the quiz page after each load, looking for
common phrasings like "You scored 85%", "8 out of 10", "Score: 90". This is
**inherently fragile** - it depends entirely on how QuizMaker (or whatever
site you point it at) words its results, and may come back blank or wrong if
the page uses different wording or renders the score as an image/canvas
instead of text. Since it now saves automatically with no confirmation step,
it's worth spot-checking the Excel file against a couple of real quiz
attempts to confirm the detected score column looks right for your quiz -
if it's consistently off, paste the exact wording your results page uses and
the detection patterns can be tuned to match.

## Safety notes — read this

I built in several layers so this can never actually strand you on your own
laptop:

- **Ctrl+Alt+Delete is untouched.** No user-mode app (this one included) can
  intercept it — Windows handles it below the application layer. That's your
  permanent backstop: it always gets you to the secure screen with Task
  Manager, sign-out, etc., no matter what bugs exist in this code.
- **You set the unlock password yourself**, each session — there's no hidden
  master password and nothing is transmitted anywhere.
- **The hotkey (Ctrl+Alt+Shift+U) is registered independently of the keyboard
  hook**, so it keeps working even while other keys are being blocked.
- **Fail-safes on exit/crash**: taskbar visibility and the keyboard hook are
  restored in `FormClosing`, `ProcessExit`, and `UnhandledException` handlers,
  so an unexpected crash doesn't leave the taskbar hidden or keys blocked.
- This only affects the app's own window and hooks — it does **not** touch
  Windows login, BitLocker, the registry, or anything at boot. Worst case if
  something goes wrong, a restart clears everything.

## Customizing the AI blocklist

Edit the `AiBlocklist` array in `MainForm.cs` to add or remove domains.

## Files

- `MainForm.cs` – setup screen + fullscreen lockdown + WebView2 + blocklist logic
- `KeyboardHook.cs` – low-level keyboard hook (Win key / Alt+Tab / Alt+F4 / Ctrl+Esc)
- `UnlockPromptForm.cs` – small password dialog shown on the unlock hotkey
- `NativeMethods.cs` – Win32 P/Invoke declarations
- `Program.cs` – entry point
