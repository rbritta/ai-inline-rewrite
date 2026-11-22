"""Smoke test of the real app on the real desktop: types into a text box of its own, uses the AIR key, pick mode,
the follow arrow, the settings and their engine picker, and checks what happened through UI Automation and the text
box's content.

It takes over the keyboard and mouse for about 3 minutes: run it on an unlocked desktop nobody is using. It stops
at once if the focus leaves its own windows (someone clicked elsewhere) or the desktop is locked. The user's AIR
settings are saved first and put back at the end.

    python tools/smoke/smoke_test.py <AiInlineRewrite.exe> <output folder for the screenshots>

Needs Python 3 with Pillow, and the engine in the settings ready (Ollama with the model, or an Anthropic key).
The engine picker checks switch to Claude and expect its no-key state, so they run only when the engine is Ollama
and this PC has no Anthropic credential; otherwise they print SKIP.
"""
import ctypes
import ctypes.wintypes as W
import os
import shutil
import subprocess
import sys
import time

from PIL import ImageGrab

import winput as wi

EXE, OUT = sys.argv[1], sys.argv[2]
HERE = os.path.dirname(os.path.abspath(__file__))
SETTINGS = os.path.join(os.environ['APPDATA'], 'AiInlineRewrite', 'settings.json')
SETTINGS_WIN = 'AI Inline Rewrite Settings'
EN = 'i dont think we can finish this until friday'
PT = 'acho que a gente precisa conversar sobre o prazo amanhã'
results = []


class Abort(Exception):
    pass


def check(name, ok, detail=''):
    results.append((name, bool(ok), detail))
    print(('PASS ' if ok else 'FAIL ') + name + (f'  [{detail}]' if detail else ''), flush=True)


def desktop_unlocked():
    h = wi.user32.OpenInputDesktop(0, False, 0x0100)
    if not h:
        return False
    wi.user32.CloseDesktop(h)
    title = ctypes.create_unicode_buffer(256)
    wi.user32.GetWindowTextW(wi.user32.GetForegroundWindow(), title, 256)
    return title.value != 'Windows Default Lock Screen'  # the lock screen, before the password prompt


def guard(pids):
    if not desktop_unlocked():
        raise Abort('the desktop is locked')
    pid = W.DWORD()
    wi.user32.GetWindowThreadProcessId(wi.user32.GetForegroundWindow(), ctypes.byref(pid))
    if pid.value not in pids:
        raise Abort(f'the focus left the test windows (foreground process {pid.value})')


wi.user32.WindowFromPoint.argtypes = [W.POINT]
wi.user32.WindowFromPoint.restype = W.HWND


def aim(x, y, pids):
    """Stops when another window (an updater, a notification) sits where the mouse is about to press."""
    pid = W.DWORD()
    wi.user32.GetWindowThreadProcessId(wi.user32.WindowFromPoint(W.POINT(x, y)), ctypes.byref(pid))
    if pid.value not in pids:
        raise Abort(f'another window (process {pid.value}) covers the point to click, {x},{y}')


def anthropic_credential():
    """Whether AIR, started from here, would find an Anthropic credential: the variables the SDK reads, or a profile
    such as the one `ant auth login` saves."""
    names = ('ANTHROPIC_API_KEY', 'ANTHROPIC_AUTH_TOKEN', 'ANTHROPIC_PROFILE', 'ANTHROPIC_FEDERATION_RULE_ID')
    if any(os.environ.get(n) for n in names):
        return True
    config = os.environ.get('ANTHROPIC_CONFIG_DIR') or os.path.join(os.environ['APPDATA'], 'Anthropic')
    return os.path.isdir(os.path.join(config, 'configs')) or os.path.exists(os.path.join(config, 'active_config'))


def shot(name, box=None):
    ImageGrab.grab(bbox=box, all_screens=True).save(os.path.join(OUT, name))


def main():
    if not desktop_unlocked():
        print('The desktop is locked: nothing to drive. Unlock it and run again.')
        return 2
    os.makedirs(OUT, exist_ok=True)
    backup = None
    if os.path.exists(SETTINGS):
        backup = os.path.join(OUT, 'settings.backup.json')
        shutil.copyfile(SETTINGS, backup)

    air = subprocess.Popen([EXE, '--accept-injected'])
    tgt = subprocess.Popen(['powershell', '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', os.path.join(HERE, 'target.ps1')])
    pids = {air.pid, tgt.pid}
    try:
        run(air.pid, tgt.pid, pids)
    except Abort as e:
        check('run to the end', False, str(e))
    finally:
        air.kill()
        subprocess.run(['taskkill', '/PID', str(tgt.pid), '/T', '/F'], capture_output=True)
        time.sleep(0.5)
        if backup:
            shutil.copyfile(backup, SETTINGS)
    failed = [r for r in results if not r[1]]
    print(f'\n{len(results) - len(failed)} of {len(results)} checks passed')
    return 1 if failed else 0


def run(air_pid, tgt_pid, pids):
    form = wi.find_window('AIR smoke test')
    edit = wi.child_edit(form)
    l, t, r, b = wi.rect(form)
    time.sleep(3)

    def ui():
        return [e for e in wi.uia(air_pid) if e['win'] == 'AI Inline Rewrite']

    def status():
        return ' '.join(e['name'] for e in ui() if e['type'] == 'Text' and e['name'].startswith('·'))

    def rows():
        return [e for e in ui() if e['type'] == 'ListItem']

    def row_text(e):
        return e['name'].split(': ', 1)[1]

    def button(name):
        return next(e for e in ui() if e['type'] == 'Button' and e['name'] == name)

    def center(e):
        return e['x'] + e['w'] // 2, e['y'] + e['h'] // 2

    def click(x, y):
        aim(x, y, pids)
        wi.click(x, y)

    def wait_ready(timeout=90):
        end = time.time() + timeout
        while time.time() < end:
            s = status()
            if any(k in s for k in ('corrected', 'Translated', 'Looks right', 'not answering', 'model')):
                return s
            time.sleep(1)
        return status()

    def focus_field():
        click((l + r) // 2, (t + b) // 2)
        guard(pids)

    def fresh(text):
        wi.set_text(edit, '')
        focus_field()
        wi.press('end')
        guard(pids)
        wi.type_text(text)
        return wait_ready()

    def tap():
        guard(pids)
        wi.press('rctrl')
        time.sleep(0.4)

    def picking():
        return 'Esc' in status()

    def selected():
        return next((i for i, e in enumerate(rows()) if e['status'] == 'Selected'), None)

    def settings_ui():
        return [e for e in wi.uia(air_pid) if e['win'] == SETTINGS_WIN]

    def settings_say(text):
        return any(e['type'] == 'Text' and e['name'] == text for e in settings_ui())

    def within(seconds, probe):
        end = time.time() + seconds
        while True:
            if probe():
                return True
            if time.time() > end:
                return False
            time.sleep(0.25)

    # the follow arrow: on, so the bubble comes to the text cursor
    focus_field()
    fol = button('Follow the text cursor')
    if fol['toggle'] != 'On':
        click(*center(fol))
        time.sleep(0.6)
    check('follow arrow on', button('Follow the text cursor')['toggle'] == 'On')

    # English: corrected versions, pick mode with the arrows and Enter
    s = fresh(EN)
    rs = rows()
    check('English sample written', 'corrected' in s and len(rs) >= 3, f'{s} | ' + ' / '.join(row_text(x) for x in rs))
    bub = wi.find_window('AI Inline Rewrite')
    shot('1-english.png', wi.rect(bub))
    tap()
    check('AIR key opens pick mode on the first version', picking() and selected() == 0, status())
    shot('2-pick.png', wi.rect(bub))
    guard(pids); wi.press('up')
    check('up on the first goes to the last', selected() == len(rs) - 1, str(selected()))
    guard(pids); wi.press('down')
    check('down on the last goes back to the first', selected() == 0, str(selected()))
    guard(pids); wi.press('down')
    want = row_text(rows()[1])
    guard(pids); wi.press('enter')
    time.sleep(1.2)
    got = wi.get_text(edit)
    check('Enter applies the selected version', got == want, f'{got!r} vs {want!r}')
    shot('3-applied.png', (l, t, r, b))
    guard(pids); wi.chord('ctrl', 'z')
    time.sleep(0.4)
    check('Ctrl+Z brings the typed sentence back', wi.get_text(edit) == EN, repr(wi.get_text(edit)))

    # Portuguese: translated, a number applies directly
    s = fresh(PT)
    rs = rows()
    check('Portuguese sample translated', 'Translated' in s and len(rs) >= 3, f'{s} | ' + ' / '.join(row_text(x) for x in rs))
    shot('4-portuguese.png', wi.rect(bub))
    want = row_text(rs[0])
    tap()
    guard(pids); wi.press(0x31)
    time.sleep(1.2)
    check('1 applies the first version', wi.get_text(edit) == want, repr(wi.get_text(edit)))

    # Esc closes, another key types, a chord is not a tap, 4 s close it
    fresh(EN)
    tap()
    guard(pids); wi.press('esc')
    check('Esc closes pick mode and leaves the text', not picking() and wi.get_text(edit) == EN, status())
    tap()
    guard(pids); wi.type_text('x')
    time.sleep(0.4)
    check('another key closes pick mode and types', not picking() and wi.get_text(edit) == EN + 'x', repr(wi.get_text(edit)))
    guard(pids); wi.chord('rctrl', 'a')
    time.sleep(0.4)
    check('Right Ctrl in a chord is not a tap', not picking(), status())
    tap()
    time.sleep(4.6)
    check('pick mode closes after 4 s without a key', not picking(), status())

    # drag the bubble: follow turns off; the arrow turns it back on
    air_label = next(e for e in ui() if e['type'] == 'Text' and e['name'] == 'AIR')
    x, y = center(air_label)
    aim(x, y, pids)
    wi.drag(x, y, x + 40, y + 160)
    time.sleep(0.6)
    check('dragging the bubble turns follow off', button('Follow the text cursor')['toggle'] == 'Off')
    shot('5-dragged.png', wi.rect(bub))
    click(*center(button('Follow the text cursor')))
    time.sleep(0.6)
    check('the arrow turns follow back on', button('Follow the text cursor')['toggle'] == 'On')

    # settings from the gear, closed with its X
    click(*center(button('Settings')))
    time.sleep(1.2)
    sw = wi.user32.FindWindowW(None, 'AI Inline Rewrite Settings')
    check('the gear opens the settings', bool(sw) and wi.user32.IsWindowVisible(sw))
    if sw:
        # beside the bubble: side by side, overlapping in height (both windows have transparent room for the shadow)
        k = wi.user32.GetDpiForWindow(bub) / 96
        bl, bt, br, bb = [v + d * k for v, d in zip(wi.rect(bub), (20, 20, -20, -20))]
        sl, st, sr, sb = [v + d * k for v, d in zip(wi.rect(sw), (24, 24, -24, -24))]
        check('the settings open beside the bubble', (sl >= br or sr <= bl) and st < bb and sb > bt,
              f'bubble {bl:.0f},{bt:.0f},{br:.0f},{bb:.0f} settings {sl:.0f},{st:.0f},{sr:.0f},{sb:.0f}')
    switched = False
    if sw:
        shot('6-settings.png', wi.rect(sw))

        # the engine picker, in the running app: Claude (no credential here, so its no-key state at once), then back
        ollama = next((e for e in settings_ui() if e['type'] == 'RadioButton' and e['name'] == 'Ollama'), None)
        if anthropic_credential() or not ollama or ollama['toggle'] != 'On':
            print('SKIP engine picker: it needs Ollama as the engine and no Anthropic credential on this PC', flush=True)
        else:
            guard(pids)
            pressed = wi.uia_press(air_pid, SETTINGS_WIN, 'Claude', 'RadioButton')
            check('Engine Claude: the settings say No Anthropic API key at once',
                  pressed and within(5, lambda: settings_say('No Anthropic API key')), f'pressed {pressed}')
            check('Engine Claude: the bubble says No Anthropic API key, same process, no restart',
                  within(5, lambda: 'No Anthropic API key' in status()), status())
            model = next((e for e in settings_ui() if e['name'] == 'Model'), None)
            check('Engine Claude: the model list offers the Claude models',
                  model is not None and (model['status'] or '').startswith('Claude'), model and model['status'])
            shot('7-settings-claude.png', wi.rect(sw))
            guard(pids)
            pressed = wi.uia_press(air_pid, SETTINGS_WIN, 'Ollama', 'RadioButton')
            switched = pressed
            check('Engine Ollama again: the settings say Ready',
                  pressed and within(15, lambda: settings_say('Ready')), f'pressed {pressed}')

        close = next(e for e in settings_ui() if e['name'] == 'Close')
        click(*center(close))
        time.sleep(0.6)
        check('the X closes the settings', not wi.user32.IsWindowVisible(sw))

    if switched:
        s = fresh(EN)
        rs = rows()
        check('Engine Ollama again: a fresh sample comes back corrected', 'corrected' in s and len(rs) >= 3,
              f'{s} | ' + ' / '.join(row_text(x) for x in rs))


if __name__ == '__main__':
    sys.exit(main())
