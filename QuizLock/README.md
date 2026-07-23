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

## Using it - single laptop (no network sharing)

1. Run `QuizLock.exe`.
2. Leave "Quiz Station" selected (it's the default).
3. Paste the quiz URL (e.g. your QuizMaker link, `https://take.quiz-maker.com/...`).
4. Set an unlock password — **you need this**, so don't forget it.
5. **Strict mode is checked by default** — this restricts navigation to only
   the quiz's own site (and common SSO login redirects like Google/Microsoft
   sign-in). Uncheck it only if you want other sites reachable too.
6. Set the Results Excel file path (defaults to your Documents folder).
7. Leave "Send results to a Collector" blank.
8. Optionally set an auto-unlock time limit.
9. Click **Start Lockdown**. This immediately:
   - Goes fullscreen with no window border/taskbar
   - Hides the Windows taskbar
   - Blocks the Windows key, Alt+Tab, Alt+F4, and Ctrl+Esc (so nothing else
     can be switched to or opened)
   - Disables right-click and devtools inside the quiz view
   - Restricts navigation to only the quiz's own domain (strict mode) and
     always blocks known AI-assistant sites regardless of strict mode
10. It'll ask for the quiz taker's name before the lockdown engages.
11. When done (or to bail early), press **Ctrl+Alt+Shift+U**, enter the
    password, and it unlocks — automatically logging name + auto-detected
    score + quiz link + timestamp to the Excel file, no confirmation prompt.

## Using it - multiple laptops sharing one Excel file over the network

If several laptops are running quizzes at once and you want all their
results in **one shared Excel file**, one laptop acts as a "Collector" and
the rest act as "Quiz Stations" that send their results to it.

### On the Collector laptop (run this one first)
1. Run `QuizLock.exe`, select **"Results Collector"**.
2. Set the Results Excel file path — this is where every laptop's results
   will end up.
3. Leave the port at `5005` unless it's already in use.
4. Click **Start Collector**. It'll show something like:
   `Listening on http://192.168.1.10:5005`
5. **Write down that address** - you'll type it into each Quiz Station.
6. If Windows Firewall prompts you, allow access on **Private networks**.
   Make sure this laptop's network is set to "Private" (not "Public") in
   Windows network settings, or other laptops on the network won't be able
   to reach it.
7. Leave this window open and running for the whole session - closing it
   stops the collector.

### On each Quiz Station laptop
1. Run `QuizLock.exe`, leave **"Quiz Station"** selected.
2. Fill in the quiz URL, password, etc. as normal.
3. In **"Send results to a Collector"**, type the address from step 4 above
   (e.g. `192.168.1.10:5005`).
4. Click **Start Lockdown** as normal.
5. On unlock, the result is sent straight to the Collector's Excel file over
   the network instead of being saved locally. If the Collector can't be
   reached (wrong address, it's offline, firewall blocking it), you'll get a
   warning and the result is saved to this laptop's own local Excel file
   instead as a fallback - you'd then need to manually merge that file in.

### Requirements for the networked mode
- All laptops must be on the **same local network** (same Wi-Fi/router) -
  this does not work over the internet or across different networks.
- The Collector laptop needs Windows Firewall to allow inbound connections
  on the chosen port - set the network profile to **Private**, not Public.
- Both roles need admin rights to run properly (the app already requests
  this via its manifest).

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
