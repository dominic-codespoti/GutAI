# Correction analytics

This console tool is admin/operator-only by construction: it requires Azure Table Storage credentials (`--connection` or `GUTAI_STORAGE_CONNECTION`) and scans draft records across user partitions. Treat its report as sensitive operational data. The tool only queries storage; `--out` writes a local JSON report file, never to Table Storage.

Run from the repository root:

```sh
dotnet run --project backend/tools/CorrectionAnalytics -- [--connection "<storage connection string>" | env GUTAI_STORAGE_CONNECTION] [--since <ISO date>] [--min-samples n] [--out <report.json>]
```

The default analysis window is the preceding 90 days, matching decision D8: committed meal drafts are retained for 90 days. Supply `--since` to use a different UTC lower bound. `--min-samples` defaults to 20. The report groups committed correction deltas by portion food class and confidence tier, joins item IDs to their grounding method, and reports draft acceptance by origin (including `suggestion`). Calibration output is a JSON object shaped for `MealScan:PortionCalibration`; review and explicitly approve it before applying it to production configuration.

The process exits with code `0` on success. It exits with code `2` for invalid or incomplete arguments, missing/invalid configuration (including the storage connection), or a storage/runtime failure.

Standard output always prints the text report and the calibration configuration JSON snippet. `--out <report.json>` additionally writes a separate JSON report containing the analysis timestamp/window and report data; it does not redirect or replace the standard-output report.

The calibration snippet has the `MealScan:PortionCalibration` shape and includes a required non-empty `Version` value as well as `Factors`. Portion calibration only applies when `Features:PortionCalibration` is enabled and this version is configured; without it, calibration is a no-op. Choose and review the version before applying the snippet in production.
