# Decision log format

Not legal advice. This page describes a file format. It does not say what binds any app, and nothing here means that writing this log, or any setting of it, makes an app compliant with a law.

Format version: 1 (draft). Written 2026-10-25 from the library's behaviour on that date. Changes after version 1 are additive only: a new allowed value for an existing field, documented on this page, and the version number goes up with it. A new field is never added; the log keeps exactly five fields. A reader built for an older version drops, on read, any line that holds a value it does not know (see [Reading](#reading)), so update readers before writers start using a new value. This page is the one place the format is defined; the C# library is one implementation of it. A developer in Kotlin, Swift, Dart or JavaScript can produce the same log from this page alone.

## Why the format is this small

The log records what an app decided after an age signal, and keeps as little as possible about the person: five fields, fixed shapes, no identifiers. A field that could hold a birthdate, an install, account or device identifier, or free text does not exist.

## The record

One record is one JSON object with exactly these five keys. Key names are case-sensitive. Every value is a JSON string (never a number, boolean or null).

| Key | Meaning | Example |
|---|---|---|
| `timestampUtc` | When the decision was made, UTC | `2026-10-24T12:00:00Z` |
| `ageBucket` | The age range the app chose to record | `13-15`, `0-17`, `18+`, `withheld`, `unknown` |
| `decision` | What the app decided | `Blocked` |
| `appVersion` | The app's version | `1.4.2`, `2.0.0-beta.1` |
| `ruleId` | A short label of your own for the rule the decision was made under | `gate-v1`, `unspecified` |

**A writer emits these five keys and no others**, in this order, with no whitespace between tokens. No sixth field is allowed, whatever it is called.

### `timestampUtc`

- Writers emit whole seconds and the offset `Z`: `yyyy-MM-ddTHH:mm:ssZ`, for example `2026-10-24T12:00:00Z`. A fraction is not written, because less precise times are harder to link to a person. (The C# writer truncates to whole seconds.) Digits are ASCII, the clock is 24-hour, and the date must be a real calendar date.
- Readers accept this grammar and a valid date and time:

```
^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|\+00:00)\z
```

That is seconds, an optional fraction of 1 to 7 digits (trailing zeros allowed on read; a dot with no digits is not valid), then `Z` (upper case) or `+00:00`. Any other offset, no offset, a lower-case `z`, a space instead of `T`, leading or trailing spaces, a trailing newline and a time without seconds are all rejected. A reader that keeps a fraction must not write it back out.
- The date and time must be valid: proleptic Gregorian, UTC, month 01 to 12, a day that exists in that month and year, hour 00 to 23, minute and second 00 to 59 (no leap second `:60`, no `24:00`), year not 0000. `0001-01-01T00:00:00Z` exactly (the "no time" value) is rejected.
- Writers truncate (floor) to whole seconds and never round: `12:00:00.9999999` is written as `12:00:00Z`.

### `ageBucket`

Exactly one of:

- `unknown`: no age range was received (the person did not share one, the platform had none, or the call failed). It means strictly "no range received".
- `withheld`: an age range was received and not recorded, either by the app's setting or because it cannot be written in the forms below (for example equal bounds, a bound of 100 or more, or lower above upper). It tells a reader that a signal existed and was redacted, as opposed to no signal.
- `<lo>-<hi>`: a range, `lo` lower than `hi`, for example `13-15` or `0-17`.
- `<lo>+`: from an age upward, for example `18+`.

`lo` and `hi` are whole years of 1 or 2 ASCII digits (`0` to `99`). Writers emit them without leading zeros; readers accept leading zeros such as `05-10`. Ranges of any width are valid, so a writer may record a wider range than the platform sent (for example `0-17`) to keep less. `0-17` is the spelling for "under 18"; `under-18`, `under18` and other words are rejected. A one-year range such as `7-7` is rejected, because it is an exact age. The values `unknown` and `withheld` are lower case only: `Unknown`, `UNKNOWN`, `Withheld` and `WITHHELD` are rejected.

The verbatim pattern (JavaScript and Dart have no `\z`: use `$` without the multiline flag; named groups are optional; the named groups are only there to compare `lo` and `hi`; the numeric rule `lo < hi` is applied after the match):

```
^(unknown|withheld|(?<lo>[0-9]{1,2})-(?<hi>[0-9]{1,2})|[0-9]{1,2}\+)\z
```

### `decision`

Exactly one of these four strings, case-sensitive: `NotApplicable`, `Allowed`, `AllowedWithParentalConsent`, `Blocked`. Numbers, other casing, lists and padded text are rejected.

### `appVersion`

```
^[0-9]{1,5}(\.[0-9]{1,5}){0,3}(-(alpha|beta|rc|preview|dev)(\.?[0-9]{1,2})?)?\z
```

Up to four numeric parts and an optional pre-release tag. No free-text suffix, no `+build` metadata, no build number in brackets. Examples that pass: `1`, `1.4.2`, `2.0.0.1`, `2.0.0-beta.1`, `3.1-rc2`. Examples that fail: `1.4.2 (57)`, `1.0-debug`, `1.4.2+9`, `v1.4.2`.

### `ruleId`

Rule ids are your own labels; the library maps no rule to any law. A rule id should not name an age group (for example `teen` or `under18`), because it would leak the category that `withheld` is meant to keep out of the log.

All of the following must hold:

```
^[A-Za-z][A-Za-z0-9._:-]{0,47}\z
```

and the value must not match either of these anywhere in it:

```
[0-9]{8,}|[0-9]{2,4}[.:_/-][0-9]{1,2}[.:_/-][0-9]{1,4}
[0-9A-Fa-f]{8,}
```

The first rejects a run of 8 or more digits and date shapes such as `2010-05-01` or `2010.05.01` (a birthdate). The second rejects a run of 8 or more hexadecimal characters (a GUID or hash fragment, which could identify an install). So a `ruleId` starts with a letter, is 1 to 48 characters from `A-Z a-z 0-9 . _ : -`, and cannot carry a date, a long number or an identifier. Examples that pass: `gate-v1`, `signup-v1`, `unspecified`. Examples that fail: `2027-gate`, `gate-20100501`, `deadbeef`, `a1b2c3d4e5`.

The `\z` in every pattern means end of text: a trailing newline is not accepted. Use your language's equivalent (`\z`, `$` in a mode that matches only at the very end, or compare the whole string). `[0-9]` means ASCII digits only; do not use `\d`, which matches other scripts' digits in several languages.

## JSON encoding

- Strings only, UTF-8, no byte-order mark when writing.
- Every written value is ASCII from the allowed sets above, so no escaping is ever needed. In particular `+` is written literally (`"ageBucket":"18+"`), not as `\u002B`. A library that escapes `+` by default must be switched to write it literally so all writers produce the same bytes.
- Readers must understand standard JSON string escapes, so a hand-edited `18\u002B` reads as `18+`.
- Keys are written in the order `timestampUtc`, `ageBucket`, `decision`, `appVersion`, `ruleId`, with `:` between a key and its value and `,` between members, and no whitespace anywhere outside strings.

## Files

**The log file is JSON Lines**: one object per line, UTF-8, each line ending in `\n` (not `\r\n`), including the last line. An empty log is an empty file, or no file. A line is never split across lines and the objects are never wrapped in an array.

**The export is one JSON array** of the same objects, in the same order, written on one line with no whitespace between tokens and no trailing newline: `[{...},{...}]`. An empty log exports as `[]`.

### Reading

How this library reads a log:

- A leading UTF-8 byte-order mark is skipped. Empty and whitespace-only lines are skipped. A line that ends in `\r\n` is read.
- A line that is not valid JSON, is not an object, is missing one of the five keys, repeats a key (a duplicate key rejects the line), has a value that is not a string, or has a value that fails the rules above is dropped, and the other lines are still read.
- **Future timestamps and long lines (file log).** This library's file log drops, on read, an entry whose timestamp is more than 1 day after the reader's clock, and any line longer than 512 bytes (not counting the final `\n`; a `\r` before it counts); a valid entry is well under 400 bytes. Its writer refuses an entry more than a day in the future, but keeps such rows already in the file when it rewrites it (they are only hidden on read). These are limits of the file log, not of the entry format; a reader in another language may apply the same limits.
- **Extra keys** on an otherwise valid line are ignored, not kept: they are never returned, never written back, and never exported. A hand-edited file therefore cannot smuggle a sixth field into an export. Key names that differ in casing (`TimestampUtc`) are extra keys, so the line is missing a required key and is dropped.
- Other tools may be stricter (see [Reading](#reading)), for example rejecting a whole file when any line is invalid or has an extra key. That is allowed. Writers must never emit anything a strict reader would reject.

## Limits

- Limits belong to the file log, not to a record. Defaults: **count** 1000 entries and **age** 365 days (an entry whose timestamp is earlier than now minus 365 days is dropped). The file log accepts other limits, larger or smaller. An in-memory log applies none.
- Order: drop entries older than the age limit first, then keep the last N by position in the file.
- Limits apply when writing (the file is rewritten without the entries past them) and when reading (entries past them are not returned). A strict reader must not reject a file for holding more than 1000 records.
- Implementation notes (not part of the format): the C# writer appends by writing the whole file to a temporary file named `<log file name>.<32 hex characters>.tmp` in the same folder and then replacing the log file; clearing the log deletes the file and any leftover temporary files of that shape.

## Choosing what to record: coarsen or withhold

A writer decides what to put in `ageBucket` at the moment it writes:

- record the range as received (`13-15`),
- record a wider range (`0-17`), or
- record `withheld` when a range was received, or `unknown` when none was.

In the C# library this is the `AgeBucketLogging` option: `Exact` (the default; the received range) or `Withheld` (`withheld` when a range was received, `unknown` otherwise; under `Exact` a received range that cannot be written is also `withheld`). See `docs/retention.md` for the background.

**A writer never changes entries it has already written.** Coarsening or withholding applies to the entry being written. Changing the setting leaves earlier entries as they are, so a log can hold a mix of values.

`withheld` hides the range but not the decision: `Blocked` or `AllowedWithParentalConsent` can still imply an age group. For a `withheld` row the inference is exact under the default policy, because `withheld` is only written for a received range. The format does not hide that, and it is up to the app to decide whether to log at all.

## What must never be stored

In the log, in an export, in tests, in samples:

- a birthdate or date of birth, or an exact age (a one-year range),
- an install ID, account ID, user ID, device identifier or advertising ID,
- the age-source tier or verification method the platform reported,
- platform error or exception text,
- any sixth field, whatever its name.

A value that fits a field's shape can still hold something unintended (for example a `ruleId` that names a person). The shapes reduce that risk; they do not remove it.

## Example (golden fixture)

Five entries, one for each decision and each special bucket. The JSON Lines block is the exact content of a log file, each line ending in `\n`:

```jsonl
{"timestampUtc":"2026-10-24T12:00:00Z","ageBucket":"13-15","decision":"AllowedWithParentalConsent","appVersion":"1.4.2","ruleId":"gate-v1"}
{"timestampUtc":"2026-10-24T12:01:00Z","ageBucket":"18+","decision":"Allowed","appVersion":"1.4.2","ruleId":"gate-v1"}
{"timestampUtc":"2026-10-24T12:02:00Z","ageBucket":"0-17","decision":"Blocked","appVersion":"2.0.0-beta.1","ruleId":"signup-v1"}
{"timestampUtc":"2026-10-24T12:03:00Z","ageBucket":"withheld","decision":"Allowed","appVersion":"2.0.0-beta.1","ruleId":"gate-v1"}
{"timestampUtc":"2026-10-24T12:04:00Z","ageBucket":"unknown","decision":"NotApplicable","appVersion":"2.0.0-beta.1","ruleId":"unspecified"}
```

The matching export (one line, no trailing newline):

```json
[{"timestampUtc":"2026-10-24T12:00:00Z","ageBucket":"13-15","decision":"AllowedWithParentalConsent","appVersion":"1.4.2","ruleId":"gate-v1"},{"timestampUtc":"2026-10-24T12:01:00Z","ageBucket":"18+","decision":"Allowed","appVersion":"1.4.2","ruleId":"gate-v1"},{"timestampUtc":"2026-10-24T12:02:00Z","ageBucket":"0-17","decision":"Blocked","appVersion":"2.0.0-beta.1","ruleId":"signup-v1"},{"timestampUtc":"2026-10-24T12:03:00Z","ageBucket":"withheld","decision":"Allowed","appVersion":"2.0.0-beta.1","ruleId":"gate-v1"},{"timestampUtc":"2026-10-24T12:04:00Z","ageBucket":"unknown","decision":"NotApplicable","appVersion":"2.0.0-beta.1","ruleId":"unspecified"}]
```

A conforming implementation in any language, given these five entries in this order, writes these exact bytes. All data here is made up.

## Reference

This page is the single reference for the format; other implementations follow it, and any reader may be stricter (see [Reading](#reading)). In this repository, a unit test reads this page and checks that the golden blocks above equal what the C# writer and exporter produce, and that the patterns on this page are the ones the code validates with.
