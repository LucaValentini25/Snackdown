## What

<!-- One or two sentences. What does this branch change? -->

## Why

<!-- The reasoning, especially for anything non-obvious. If a simpler approach was rejected, say
     why here — that decision is worth more than the diff. -->

## How it was verified

<!-- Be specific and honest. "Tested" says nothing; "host + 1 client under 150 ms simulated
     latency, corrections visible in the overlay, none felt" says everything.
     List what you did NOT verify too. -->

- [ ] Compiles with no errors and no new warnings
- [ ] Unity console clean after a play session
- [ ] **EditMode tests run and green** (Test Runner → EditMode → Run All; they take under a second)
- [ ] Tested with more than one peer, **through a real join** (if it touches netcode) — invoking a
      callback directly skips the handshake, and the handshake is where a join bug hid for 47 hours
- [ ] `README.md` still true (does this change how the project is run, or what state it is in?)

## Notes

<!-- Follow-ups, known gaps, anything the next person should know. Delete if empty. -->
