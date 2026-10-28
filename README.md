# TA Timesheet

A Windows 11 desktop app for logging hours and producing the Turner Automation **Customer Timesheet**: the same landscape, two-week, logo-in-the-corner Word document as `TA_Timesheet_MMDDYY.docx`, without filling in tables by hand.

- **Dropdown history** for Project, Location, Customer and Name. Start typing and it autocompletes. Picking a project fills in the location you last used with it.
- **Start/End time dropdowns** list your most-used times at the top. You can also type them: `6`, `0630`, `6:30`, `6:30p` all work.
- **Hours calculate themselves**, including shifts that run past midnight. Week and period totals are added up automatically.
- **Mismatch warnings.** If the hours on a row don't match its start and end times, the Hours cell turns orange.
- **Exports:**
  - **Word timesheet (.docx)**: matches the original layout, with logo, Name/Customer box, Week 1 / Week 2 tables and totals.
  - **PDF**: the same timesheet, ready to email.
  - **Print / Print preview**.
  - **Excel (.xlsx)**: every entry in any date range, plus a summary sheet with hours by project and by week.
  - **CSV**: the same entries as plain text.
- **Import** old Word timesheets (`TA_Timesheet_*.docx`, several at once) or CSV files. Entries that are already in the app are skipped.
- **Saves on every change.** A backup is made automatically each day.

## Install (Windows 10 / 11)

1. Download [`dist/TATimesheet.exe`](dist/TATimesheet.exe). On GitHub, open the file and click **Download raw file**.
2. Put it somewhere permanent, for example `Documents\TA Timesheet\`. Right-click it and choose **Pin to taskbar** if you like.
3. Double-click it.
   - If Windows says **"Windows protected your PC"**, click **More info**, then **Run anyway**. This happens because the app isn't code-signed.
   - If it still won't open, right-click the exe, choose **Properties**, tick **Unblock**, then click **OK**.

Nothing else needs to be installed. The app uses the .NET Framework 4.8 that comes with Windows 10 and 11.

## Using it

1. **First run:** the app offers to import your old Word timesheets. Say yes and select all the `TA_Timesheet_*.docx` files. This fills in your history and dropdowns.
2. **Log a day:** the date already points at the next empty day. Check the project and location, set Start and End, add a note if you want one, and press **Enter**. The entry is saved and the date moves to the next day.
3. **Fix something:** click a row in the list to edit it. Press **Delete** to remove it, or right-click and choose **Copy to next day**. **Esc** stops editing.
4. **Send the timesheet:** click **Word timesheet** or **PDF** at the bottom. The file name defaults to `TA_Timesheet_102626.docx` (the period's start date) and the file opens when it's done.

Name and Customer at the top right are saved per pay period. They're what gets printed on that period's timesheet.

| Shortcut | Does |
|---|---|
| Enter / Esc | Save entry / stop editing |
| Ctrl+L | Copy your last entry into the form |
| Ctrl+PgUp / Ctrl+PgDn / Ctrl+T | Previous / next / current pay period |
| Ctrl+E / Ctrl+Shift+E | Export Word / PDF |
| Ctrl+P | Print preview |
| Ctrl+H | Manage dropdown history |
| F1 | Help |

**Tools > Settings** controls the pay period length (default 2 weeks starting Monday 10/26/2026), the time-dropdown step, the export folder, the file name prefix and the logo.

## Your data

Everything lives in one file: `%APPDATA%\TA Timesheet\timesheet-data.xml`. Click **Data folder** in the bottom-right corner to open it.

- A copy is saved every day into the `Backups` folder next to it, and the last 30 days are kept.
- **File > Back up data to...** saves a copy anywhere, such as OneDrive or a USB stick.
- **File > Restore from backup...** puts one back. The app saves what's currently in it first.

## Building from source

The exe in `dist/` is prebuilt. To rebuild it:

- **Windows:** double-click `build.bat`. It uses the C# compiler that ships with Windows, so Visual Studio isn't needed.
- **Linux/macOS (Mono):** run `./build.sh`. To also run the headless tests against a real timesheet:
  `./build.sh test path/to/TA_Timesheet_102626.docx`

The code is plain C# 5 / WinForms on .NET Framework 4.x with no NuGet packages. The Word export starts from `src/Resources/TimesheetTemplate.docx`, which keeps the original header, logo, styles and page setup. `tools/make_resources.py` regenerates that template and the app icon from any real timesheet .docx.

| Path | What |
|---|---|
| `src/MainForm*.cs` | Main window and its actions |
| `src/Models.cs`, `src/TimeUtil.cs` | Entries, pay periods, time parsing |
| `src/DataStore.cs` | Saving, loading and daily backups |
| `src/DocxExporter.cs` | Word timesheet |
| `src/SheetLayout.cs`, `src/PdfExporter.cs`, `src/PrintSupport.cs` | PDF and printing (shared page layout) |
| `src/DataExporters.cs` | Excel and CSV |
| `src/Importers.cs` | Word and CSV import |
| `tests/SmokeTest.cs` | Headless checks run by `build.sh test` |
