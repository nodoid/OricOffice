# Oric Office

An office suite for the modern era, inspired by the classic Oric programs:
**Author** (word processor), **Calc** (spreadsheet) and **Base** (database).

Launching the app opens a start centre with three large buttons — one per
application, each with its own icon. Documents are stored in SQLite-backed
suite files (`.oau`, `.oca`, `.oba`) that any of the three modules can open.

## Applications

| Module | Extension | What it does |
|--------|-----------|--------------|
| Author | `.oau` | Word processor with live word count, selection ops, zoom |
| Calc   | `.oca` | Spreadsheet grid, formula bar, name box, recalculation |
| Base   | `.oba` | Datasheet database: fields, records, query + sort bar |

## Stack

- .NET 10 (SDK pinned in `global.json`), .NET MAUI single project
- CommunityToolkit.Mvvm, Microsoft.Data.Sqlite
- Shared chrome (LibreOffice-style menus/toolbars/status bars) in
  `OricOffice.MauiShared`; all document logic in `OricOffice.Core`

## Targets

- **Windows** 11+ (unpackaged, runs straight from `bin`)
- **Android** API 32+, tablets only, portrait
- **iOS** 18+ / **macOS** (Mac Catalyst) 18+, portrait

App id: `uk.co.allthejohnsons.oricoffice`

## Build & test

```
dotnet build src/OricOffice/OricOffice.csproj -f net10.0-windows10.0.22621.0
dotnet test tests/OricOffice.Core.Tests
```

## License

DILLIGAF — see [LICENSE](LICENSE).

Copyright © Paul F. Johnson.
