# Instance lifecycle

An instance report uses five lifecycle states:

- `Starting`: the operating-system process was spawned, but its lifecycle observer has not reported ready.
- `Running`: the lifecycle observer reported ready.
- `Stopping`: a cooperative stop was requested.
- `Stopped`: the process exited without an observer-confirmed crash, or a halt committed this terminal state first.
- `Crashed`: the observer confirmed a crash before another terminal state was committed.

The default ready timeout is exactly two minutes. A timeout does not terminate the process and does not change `Starting` into another status. Instead, reports and instance-catalog snapshots expose `ready_timed_out: true`. The flag clears on the next lifecycle transition.

Generic processes become `Running` when their process-ready signal is observed. Minecraft Java processes become `Running` only after a stdout line matches this expression:

```regex
Done \(\d+\.\d{1,3}s\)! For help, type ["']help["'](?:\s+or\s+["']\?["'])?\z
```

The implementation applies `TrimEnd` before matching. A normal logger prefix is allowed, but the canonical ready text must be the complete suffix of the line. It does not accept trailing or embedded text, integer seconds, more than three fractional digits, unquoted `help`, or case changes to the canonical text.

Minecraft crash classification is also stdout-only. It uses an ordinal, case-sensitive `Contains("Minecraft has crashed")` check. Stderr does not participate in ready or crash classification, and an `hs_err` filename or file-presence message by itself does not produce `Crashed`.

`Crashed` and `Stopped` are absorbing terminal states: the first committed terminal fact wins. A later halt still terminates the process tree and clears the process id, but it does not rewrite `Crashed` as `Stopped` or publish a second terminal transition.

## Command completion boundaries

`instance.start` returns success after the operating-system spawn and `Starting` publication commit. It does not wait for `Running`. A process that exits immediately after this boundary still produced a successful start; its later observable state is `Stopped`.

`instance.stop` returns success after `Stopping` is committed. It does not wait for process exit or output drain. Clients must poll `instance.report` until the status becomes terminal (`Stopped` or `Crashed`). If a Minecraft process exits after the `Stopping` commit but before the daemon writes `stop` to stdin, the stop remains successful.

`instance.halt` is the stronger boundary. It does not return success until the existing operating-system process tree, redirected output pumps, and tracked lifecycle publications have drained. A process-tree termination failure returns `instance.halt_failed`; the daemon does not clear the live process binding or permit a replacement start.

The .NET daemon client's `RestartInstanceAsync` composes stop, terminal-state polling, and start. It polls once per second for at most 30 seconds. Caller cancellation is propagated unchanged. If no terminal report arrives before the deadline, it returns `instance.restart_timeout` and does not start a new process.
