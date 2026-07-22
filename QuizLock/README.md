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
6. Optionally check **"Save name + score from the quiz results page to an
   Excel file"** (see below).
7. Click **Start Lockdown**. You'll first be asked for the quiz taker's name,
   then it immediately:
   - Goes fullscreen with no window border/taskbar
   - Hides the Windows taskbar
   - Blocks the Windows key, Alt+Tab, Alt+F4, and Ctrl+Esc (so nothing else
     can be switched to or opened)
   - Disables right-click and devtools inside the quiz view
   - Restricts navigation to only the quiz's own domain (strict mode) and
     always blocks known AI-assistant sites regardless of strict mode
8. When you're done (or need to bail early), press **Ctrl+Alt+Shift+U**,
   enter your password, and it unlocks back to the setup screen.

## Saving name + score to Excel

Check **"Save name + score from the quiz results page to an Excel file"** on
the setup screen and fill in:

- **Score selector** – a CSS selector pointing at the element on your Quiz
  Maker results page that contains the score. The default
  (`#quiz-score, .result-score`) is a guess — you'll need to find the real
  one for your quiz.
- **Save results to** – the `.xlsx` file to append rows to (created
  automatically if it doesn't exist, with **Name / Score / Date-Time**
  columns). The name comes from what's typed into the "Who's taking this
  quiz?" prompt when lockdown starts — no scraping needed for that part.

**Finding the score selector:** open your quiz's results page in a normal
browser (Edge/Chrome), right-click the score and press *Inspect*, and note
its `id` or `class` in the Elements panel (e.g. `<span id="finalScore">`  →
selector `#finalScore`). DevTools are disabled inside the locked-down window
itself, so do this lookup beforehand in a regular browser tab.

Once the score element has text on the quiz's own domain, QuizLock captures
it once per session, appends a row (name + score + timestamp) to the
spreadsheet, and shows a brief on-screen confirmation. If the save fails (for
example the file is open in Excel), you'll get a message box when you unlock
explaining why.

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

- `MainForm.cs` – setup screen + fullscreen lockdown + WebView2 + blocklist + score-to-Excel logic
- `NameEntryForm.cs` – prompt for the quiz taker's name before lockdown begins
- `KeyboardHook.cs` – low-level keyboard hook (Win key / Alt+Tab / Alt+F4 / Ctrl+Esc)
- `UnlockPromptForm.cs` – small password dialog shown on the unlock hotkey
- `NativeMethods.cs` – Win32 P/Invoke declarations
- `Program.cs` – entry point
