/* ValcoNet launcher.
 * Starts the Gecko engine (engine\firefox.exe) with ValcoNet's own profile, so ValcoNet
 * never shares a profile with an installed Firefox, and sets the taskbar AppUserModelID
 * so pinned ValcoNet icons relaunch through this launcher.
 *
 *   valconet.exe [args...]   -> engine\firefox.exe -profile "<profile>" [args...]
 *
 * Profile: %APPDATA%\Valco\ValcoNet\Profile, or .\Profile when portable.ini sits next to
 * valconet.exe (portable mode).  © 2026 ItsRaiden, MPL 2.0
 */
#define WIN32_LEAN_AND_MEAN
#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#include <windows.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <wchar.h>

#define APP_ID L"ItsRaiden.ValcoNet"
#define BIG 32768

static void fail(const wchar_t *what, DWORD err) {
  wchar_t msg[1024];
  wchar_t sys[512] = L"";
  FormatMessageW(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, err, 0, sys, 512, NULL);
  _snwprintf(msg, 1024, L"ValcoNet couldn't start.\n\n%s\n%s", what, sys);
  msg[1023] = 0;
  MessageBoxW(NULL, msg, L"ValcoNet", MB_ICONERROR | MB_OK);
}

/* Gecko reads HKCU\Software\Mozilla\Firefox\TaskBarIDs\<engine dir> to pick its AppUserModelID. */
static void set_taskbar_id(const wchar_t *engineDir) {
  HKEY key;
  if (RegCreateKeyExW(HKEY_CURRENT_USER, L"Software\\Mozilla\\Firefox\\TaskBarIDs", 0, NULL, 0,
                      KEY_QUERY_VALUE | KEY_SET_VALUE, NULL, &key, NULL) != ERROR_SUCCESS) {
    return;
  }
  wchar_t cur[256];
  DWORD size = sizeof(cur), type = 0;
  if (RegQueryValueExW(key, engineDir, NULL, &type, (BYTE *)cur, &size) != ERROR_SUCCESS || type != REG_SZ ||
      wcscmp(cur, APP_ID) != 0) {
    RegSetValueExW(key, engineDir, 0, REG_SZ, (const BYTE *)APP_ID, (DWORD)((wcslen(APP_ID) + 1) * sizeof(wchar_t)));
  }
  RegCloseKey(key);
}

/* Everything after argv[0] in the raw command line, untouched (keeps the caller's quoting). */
static const wchar_t *args_after_program(const wchar_t *cmd) {
  const wchar_t *p = cmd;
  if (*p == L'"') {
    p++;
    while (*p && *p != L'"') p++;
    if (*p) p++;
  } else {
    while (*p && *p != L' ' && *p != L'\t') p++;
  }
  while (*p == L' ' || *p == L'\t') p++;
  return p;
}

int WINAPI wWinMain(HINSTANCE inst, HINSTANCE prev, PWSTR cmdLine, int show) {
  (void)inst; (void)prev; (void)cmdLine;
  static wchar_t self[MAX_PATH], dir[MAX_PATH], engineDir[MAX_PATH], engine[MAX_PATH], profile[MAX_PATH], marker[MAX_PATH];
  static wchar_t cmd[BIG];

  DWORD n = GetModuleFileNameW(NULL, self, MAX_PATH);
  if (!n || n >= MAX_PATH) {
    fail(L"Couldn't find where ValcoNet is installed.", GetLastError());
    return 1;
  }
  wcscpy(dir, self);
  PathRemoveFileSpecW(dir);

  PathCombineW(engineDir, dir, L"engine");
  PathCombineW(engine, engineDir, L"firefox.exe");
  if (GetFileAttributesW(engine) == INVALID_FILE_ATTRIBUTES) {
    fail(L"The ValcoNet engine is missing. Please reinstall ValcoNet.", GetLastError());
    return 1;
  }

  PathCombineW(marker, dir, L"portable.ini");
  if (GetFileAttributesW(marker) != INVALID_FILE_ATTRIBUTES) {
    PathCombineW(profile, dir, L"Profile");
  } else {
    wchar_t appdata[MAX_PATH];
    if (FAILED(SHGetFolderPathW(NULL, CSIDL_APPDATA | CSIDL_FLAG_CREATE, NULL, SHGFP_TYPE_CURRENT, appdata))) {
      fail(L"Couldn't find your AppData folder.", GetLastError());
      return 1;
    }
    PathCombineW(profile, appdata, L"Valco\\ValcoNet\\Profile");
  }
  SHCreateDirectoryExW(NULL, profile, NULL); /* fine if it already exists */

  set_taskbar_id(engineDir);
  SetCurrentProcessExplicitAppUserModelID(APP_ID);
  SetEnvironmentVariableW(L"MOZ_APP_LAUNCHER", self);
  SetEnvironmentVariableW(L"VALCONET_LAUNCHER", self);
  /* Crash reports from a rebranded build shouldn't go to Mozilla's crash servers. */
  SetEnvironmentVariableW(L"MOZ_CRASHREPORTER_DISABLE", L"1");

  const wchar_t *rest = args_after_program(GetCommandLineW());
  int len = _snwprintf(cmd, BIG, L"\"%s\" -profile \"%s\"%s%s", engine, profile, *rest ? L" " : L"", rest);
  if (len < 0 || len >= BIG) {
    fail(L"The command line is too long.", ERROR_INSUFFICIENT_BUFFER);
    return 1;
  }

  STARTUPINFOW si;
  PROCESS_INFORMATION pi;
  ZeroMemory(&si, sizeof(si));
  si.cb = sizeof(si);
  si.dwFlags = STARTF_USESHOWWINDOW;
  si.wShowWindow = (WORD)show;
  ZeroMemory(&pi, sizeof(pi));
  AllowSetForegroundWindow(ASFW_ANY);
  if (!CreateProcessW(engine, cmd, NULL, NULL, FALSE, 0, NULL, engineDir, &si, &pi)) {
    fail(L"Couldn't start the ValcoNet engine.", GetLastError());
    return 1;
  }
  /* Let the engine bring its window forward, then get out of the way. */
  WaitForInputIdle(pi.hProcess, 3000);
  CloseHandle(pi.hThread);
  CloseHandle(pi.hProcess);
  return 0;
}
