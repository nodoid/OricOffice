# Manual command audit — OricOffice vs. the original Tansoft manuals

Every internal command documented in the three original manuals was extracted and
compared against what OricOffice implements. Status legend:

- **✅ Replicated** — the documented command/behaviour exists in OricOffice.
- **⚠️ Adapted** — the capability exists but differs in form or semantics.
- **❌ Missing** — not implemented.
- **n/a** — hardware/tape concern with no modern equivalent.

## Sources

The manuals are the original Tansoft booklets whose scans are hosted on
oric.org (see the oric.org software entries for AUTHOR, ORIC CALC and ORIC
BASE); text was extracted from the Defence Force Oric Library PDF copies:

| Manual | Author / publisher | PDF |
|---|---|---|
| AUTHOR (word processor) | John Dawson, Tansoft, Dec 1983 (44 pp) | `library.defence-force.org/books/content/manual_tansoft_author.pdf` |
| ORIC-CALC (spreadsheet) | Geoff M. Phillips, Tansoft, 1983 (24 pp) | `library.defence-force.org/books/content/manual_tansoft_oric_calc.pdf` |
| Oricbase (database) | Geoff M. Phillips, Tansoft, 1983 (18 pp) | `library.defence-force.org/books/content/manual_tansoft_oric_base.pdf` |

Note: Oric Calc and Oric Base define **no named formula functions** — Calc's
only "function" is the `[` range-sum shorthand and Base's engine is the
`FIND … BEGIN … ATRECORD … END` sentence language. OricOffice's SUM()/AVERAGE()
and its query box are therefore supersets, not replications, of the originals.

---

## ORIC-CALC

Manual summary of commands (p.19 of the booklet) vs. `CalcViewModel` +
`FormulaEngine`:

| Manual command | Meaning | OricOffice | Status |
|---|---|---|---|
| arrow keys | move current box | grid navigation, NameBox | ✅ |
| `A` | automatic entry: enter numbers, box moves each RETURN, asks direction | typing commits and moves; no direction-prompt mode | ⚠️ |
| `B` | blank part of sheet (asks formula / data / both) | Cell ▸ Clear Cell (clears whole cell) | ⚠️ |
| `C` | copy formula to a box range, adjusting references (`=REF` pins one) | **no copy/paste in Calc**; `$C$2` absolute refs parse but nothing adjusts refs | ❌ |
| `D` | change decimal places 0–9 or `V` (variable) for the whole sheet | not implemented (values print as computed) | ❌ |
| `F` | enter a formula | formula bar, `=` prefix | ✅ |
| `G` | GOTO a box reference | NameBox / Go To (GoToAddress) | ✅ |
| `I` | **insert** space for a new row/column (shifts existing content) | Add Row/Column **appends** at the end (grows grid) | ⚠️ |
| `M` | move current row or column elsewhere | not implemented | ❌ |
| `N` | enter number into current box | typing a number | ✅ |
| `P` | print area, asks row/column headers and formula printing | Print… (fixed layout) | ⚠️ |
| `Q` | quit (confirm with `Q`) | Exit | ✅ |
| `S` | save whole or part of sheet to tape | Save / SaveAs | ✅ |
| `"` (quote) | enter text starting at current box (70 bytes along the row) | typing text into a cell | ⚠️ |
| `?` | print free memory | n/a — fixed modern grid | n/a |
| `W` | WORK OUT: recalculate (optionally an area) | recalculates on every edit; `Recalculate()` exists | ✅ |
| `L` | load a saved sheet (startup option) | Open | ✅ |
| `[AA1AT1` | **range-sum shorthand** (also rectangular: `[AB3AD5`) | not accepted; `SUM(range)` is the equivalent | ❌ syntax |
| `=AZ10` | absolute (copy-proof) reference | `$C$2` absolute syntax round-trips through ODF | ⚠️ |
| comparisons yield **−1 / 0** | `(AA2=1)` is −1 when true, enabling `AA1*(-.08*(AA2=1)-.15*(AA2=2))` | comparisons yield **1 / 0** | ⚠️ |
| errors: SYNTAX, DIVIDE BY ZERO, OVERFLOW, ILLEGAL QUANTITY, TYPE MISMATCH, OUT OF MEMORY — reported with the offending box flashing | `#ERROR?`, `#DIV/0!`, `#NAME?`, `#REF?`, `#CIRC!` shown in-cell | ✅ |
| 9-character numbers, minus sign at right, no exponent entry | modern float display | ⚠️ cosmetic |
| blank box in a formula counts as zero | blank resolves to 0 | ✅ |

Formula functions: the manual defines **none**. OricOffice's `IF, SUM, AVERAGE,
MIN, MAX, COUNT, ABS, ROUND, AND, OR, NOT, CONCAT, LEN, UPPER, LOWER` are an
addition (superset ✅).

## ORICBASE

Glossary (p.13 of the booklet) vs. `BaseViewModel` + `RecordQuery`:

| Manual command | Meaning | OricOffice | Status |
|---|---|---|---|
| `FIND field op value` (+ `AND` / `OR`, quotes around spaced values, trailing `.`) | select records | query box: same six operators (`= <> < <= > >=`) plus `contains`, `startswith`, `is blank`; AND/OR with AND binding tighter; quoted values | ✅ superset (a trailing `.` is **not** stripped — original auto-inserts one) |
| `SORA field` / `SORD field` | sort ascending / descending | Record ▸ Sort Ascending / Sort Descending | ✅ |
| `ADD` | add a new record | Record ▸ New Record | ✅ |
| `DELETE` (inside ATRECORD) | delete current record | Record ▸ Delete Record (selected) | ✅ |
| `SAVE` / load option | save data / load file | Save / SaveAs / Open | ✅ |
| `DUMP` | print whole file quickly | Print… + Export (CSV/SQL/…) | ⚠️ |
| `FIELDS` | list defined fields | field strip lists names | ⚠️ |
| `PON` / `POFF` | printer on / off | Print… / Print to File | ⚠️ |
| `BEGIN … ATRECORD (ATR) … END` | before/each-record/after report engine | not implemented | ❌ |
| `PRINT` items, `CR`, `FF` | report lines from fields, registers, quoted literals | not implemented | ❌ |
| `ADD/SUBTRACT/MULTIPLY/DIVIDE/MOVE x TO y` | per-record arithmetic | not implemented | ❌ |
| registers `#1`–`#20` | totals/averages across records | not implemented | ❌ |
| `INPUT` operand | prompt for a value at run time | not implemented | ❌ |
| `IF … ENDIF` (nestable, no AND/OR) | conditional delete/update | not implemented | ❌ |
| `MACSAV/MACRUN/MACDIS/MACDIR/MACDEL` + `&` run-time parameters | saved sentences | not implemented | ❌ |
| `EDIT` / `LAST` | amend / repeat last sentence | last query stays in the box (partial) | ❌ |
| `\` key / `ABORT` | stop a running sequence | n/a | ❌ |

Field types (ALPHA/NUMERIC with length limits, numeric fields rejecting
non-numeric input) are modelled by `FieldDef`; enforced numeric validation on
entry is partial (⚠️).

## AUTHOR

Main Command Level + Edit function + dot commands vs. `AuthorViewModel`:

| Manual command | Meaning | OricOffice | Status |
|---|---|---|---|
| `W` | write new text (confirms destroying old) | New | ✅ |
| `E` | edit | always in editing | ✅ superseded |
| `C` | continue writing at end of text | continuous editing | ✅ superseded |
| `R` | retrieve text from tape (Y confirm, name) | Open / Import from tape | ⚠️ |
| `S` | store text to tape (name ≤ 15 chars) | Save / SaveAs | ⚠️ |
| `A` | **append** a text from tape to the current text | not implemented — import/open replaces | ❌ |
| `P` | print the text | Print… | ✅ |
| `Z` | word count | live stats (words/chars/paragraphs) in status | ✅ |
| `T` | tape speed F/S | n/a | n/a |
| `I` | **Install**: page layout (margins, lines/page, form length, copies, GP vs ORIC presets, print macros P0–P9) | not implemented — no page-setup UI | ❌ |
| Control-Q | quit current function | Escape / menus | ⚠️ |
| Control-A | caps lock toggle | n/a | n/a |
| Control-Z | word count while writing/editing | stats update live | ✅ superset |
| Control-P | print screen dump | n/a | ❌ |
| Control-C + letter | embed printer control codes (I tab, J LF, L FF, M CR, [ ESC) | not implemented | ❌ |
| Control-D + letter | **dot commands** (all 19, letters verified against the page images): `.C` centre, `.E` end page, `.F` formfeed toggle, `.H` header text, `.I` indent, `.J` header toggle, `.K` copies, `.L` left margin, `.N` set page number, `.O` printer toggle, `.P 0-9` print macro, `.R` right margin, `.S` single-sheet toggle, `.U` unformat, `.V` lines per page, `.W` form length, `.X n…:` raw bytes (colon-terminated), `.#` print page number, `.*` print counter | not implemented | ❌ |
| cursor keys + `7 8 9 0` | navigate / scroll (7 = top, 0 = page down) | standard editor navigation | ✅ |
| `[Delete]` + `space` / `.` / `RETURN` | delete word / sentence / paragraph | backspace + Cut only | ⚠️ |
| `U` | **Uncover** — recover the last deleted text (delete buffer) | no undo in the rich-text editor | ❌ |
| `K` | kill rest of text (confirm `!`) | not implemented | ❌ |
| `I` (edit) | insert mode | editing always inserts | ✅ |
| `O` | overwrite mode | not implemented | ❌ |
| `M` / `T` | move block / paste move buffer (repeatable = copy) | Cut / Copy / Paste | ✅ |
| `]` | end-of-block marker for MOVE | selection model | ✅ superseded |
| `F` | **Find and Change** — up to 25 chars, `.` wildcard, Return = next, `Y` = change, then change-all | **not implemented — Author has no find/replace** | ❌ |
| `.` after Ctrl-C | text markers, `[.]` finds next | not implemented (no bookmarks) | ❌ |
| `>` after Ctrl-C | auto-insert markers; Shift-. steps through them (mail-merge slots) | not implemented | ❌ |
| `R` (edit) | read a named block from tape, **inserted at cursor** | not implemented (imports replace) | ❌ |
| `W` (edit) | write a marked block to tape | not implemented | ❌ |
| `P` (edit) | print from cursor position | not implemented | ❌ |
| word-wrap | automatic at margins | automatic wrapping | ✅ |

## Empirical note — AUTHOR's `S` (Store) capture

Verified 2026-10-21 by driving the real AUTHOR v2.01 in Oricutron: the `S`
command writes Tansoft's own tape waveform, not the standard ROM protocol — an
F9 tape-output capture of a full slow-speed save contains only the custom
$FF-byte leader (~188 bytes), the same finding as the earlier hello.ort
experiment. The document IMAGE the Store writes is nevertheless plain text
and was recovered with the emulator monitor's `wm` (write memory) command:
$3400 installation block, $3480 space-padded name, $3500 the NUL-terminated
ASCII text with control codes stored as $C0 | code. That layout is what
`OricAuthorText` parses (see tests/fixtures/author-esctest.bin).

## Highest-value gaps

1. **Author: Find & Change** — the manual's `[F]` is a core edit command; the
   app has no find/replace at all.
2. **Author: Undo** — `[U] Uncover` recovers deletions; the custom rich-text
   editor has no undo stack.
3. **Calc: `[` range-sum syntax** — accepting `=[A1A10]` alongside `SUM(A1:A10)`
   would accept real Oric Calc formula text.
4. **Calc: comparison results should be −1/0** — the manual's VAT idiom
   `AA1*(-.08*(AA2=1)-.15*(AA2=2))` evaluates wrongly with 1/0 results.
5. **Calc: insert (shift) semantics for rows/columns** — Add Row/Column
   appends; the manual's `I` inserts and shifts, and warns formulae must adapt.
6. **Calc: `C` copy-with-adjust and `D` decimal places** — both central to the
   original workflow.
7. **Base: trailing period tolerance and the sentence engine** — the query box
   covers `FIND`, but `BEGIN/ATRECORD/END`, arithmetic, registers, `IF…ENDIF`
   and macros have no equivalent.
8. **Author: Install / dot commands** — printer layout control has no
   page-setup counterpart yet.
