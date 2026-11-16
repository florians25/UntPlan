using UntPlan.Core;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Einstellungen laut Handbuch 7.4: Tageszeitraster (Schuldaten | Allgemeines), Diverses | Sichern (Sicherungsgenerationen),
/// Diverses | Stundenplan (Versions-Nummer, Datumssynchronisation, Periodenwechsel, Wochenanzeige) und Logging und Crash Reporting.
/// </summary>
public sealed partial class EinstellungenDialog
{
    /// <summary>Geänderte Option „Tageszeitraster aktivieren“ (null = unverändert); wird mit <OK> übernommen.</summary>
    private bool? _tageszeitraster;

    private void TageszeitrasterOption(int y)
    {
        var c = new CheckBox
        {
            Text = "Tageszeitraster aktivieren", Left = 292, Top = Math.Max(y, 132), AutoSize = true,
            Checked = _tageszeitraster ?? AppZustand.D.Zeitraster.TageszeitrasterAktiv,
        };
        c.CheckedChanged += (_, _) => _tageszeitraster = c.Checked;
        _seite.Controls.Add(c);
        _seite.Controls.Add(new Label
        {
            Text = "Jeder Wochentag kann eigene Beginn- und Endzeiten haben (Zeitraster, Karteikarte „Tage“).", Left = 292, Top = c.Top + 22, Width = 250, Height = 32, ForeColor = Color.DimGray,
        });
    }

    /// <summary>Sichern: Generationen (Save1 … SaveN), nur bei geänderten Daten, Mit dem aktuellen Datum starten.</summary>
    private void SichernErgaenzen(ref int y)
    {
        var e = AppZustand.Einstellungen;
        var gen = Ui.Zeile(_seite, "Anzahl der Sicherungsgenerationen (Save1 … SaveN)", new NumericUpDown { Minimum = 1, Maximum = 99, Value = Math.Clamp(e.SicherungsGenerationen, 1, 99), Font = Kursiv }, ref y, 280, 60);
        gen.ValueChanged += (_, _) => Lokal(() => e.SicherungsGenerationen = (int)gen.Value);
        var nur = Ui.Zeile(_seite, "Automatische Sicherung nur bei geänderten Daten", new CheckBox { Checked = e.SicherungNurBeiAenderung }, ref y, 280);
        nur.CheckedChanged += (_, _) => Lokal(() => e.SicherungNurBeiAenderung = nur.Checked);
        var datei = Ui.Zeile(_seite, "Zusätzlich die geöffnete Datei speichern", new CheckBox { Checked = e.SicherungInGeoeffneteDatei }, ref y, 280);
        datei.CheckedChanged += (_, _) => Lokal(() => e.SicherungInGeoeffneteDatei = datei.Checked);
        var heute = Ui.Zeile(_seite, "Mit dem aktuellen Datum starten", new CheckBox { Checked = e.MitAktuellemDatumStarten }, ref y, 280);
        heute.CheckedChanged += (_, _) => Lokal(() => e.MitAktuellemDatumStarten = heute.Checked);
        var ordner = Einstellungsseiten.Pfad("Sicherungsdateien");
        var vorhanden = Sicherungsgenerationen.Vorhandene(ordner);
        _seite.Controls.Add(new Label
        {
            Text = $"Sicherungen in {ordner}" + (vorhanden.Count > 0 ? $" – zuletzt {vorhanden[0].Zeit:dd.MM.yyyy HH:mm}" : "") +
                   ". Die neueste Sicherung liegt in Save1. Beim Programmstart <Umschalt> gedrückt halten, um die zuletzt bearbeitete Datei nicht zu öffnen.",
            Left = 12, Top = y + 4, Width = 520, Height = 46, ForeColor = Color.DimGray,
        });
        y += 52;
    }

    /// <summary>Stundenplan: Stundenplan-Versions-Nummer, Datumssynchronisation, Periodenwechsel, Wochenanzeige.</summary>
    private void StundenplanErgaenzen(ref int y)
    {
        var e = AppZustand.Einstellungen;
        var ver = Ui.Zeile(_seite, "Stundenplan-Versions-Nummer", new NumericUpDown { Minimum = 0, Maximum = 9999, Value = Math.Clamp(_e.StundenplanVersion, 0, 9999) }, ref y, 280, 60);
        ver.ValueChanged += (_, _) => _e.StundenplanVersion = (int)ver.Value;
        var sync = Ui.Zeile(_seite, "Datumssynchronisation", new CheckBox { Checked = e.Datumssynchronisation }, ref y, 280);
        sync.CheckedChanged += (_, _) => Lokal(() => e.Datumssynchronisation = sync.Checked);
        var per = Ui.Zeile(_seite, "Bei Periodenwechsel Stundenplan auf Periodenbeginn stellen", new CheckBox { Checked = e.PeriodenwechselPeriodenbeginn }, ref y, 280);
        per.CheckedChanged += (_, _) => Lokal(() => e.PeriodenwechselPeriodenbeginn = per.Checked);
        var w = new GroupBox { Text = "Wochenanzeige", Left = 12, Top = y + 4, Width = 300, Height = 64 };
        var schul = new RadioButton { Text = "Schulwochen", Left = 10, Top = 18, AutoSize = true, Checked = !e.WochenanzeigeKalenderwochen };
        var kal = new RadioButton { Text = "Kalenderwochen", Left = 10, Top = 38, AutoSize = true, Checked = e.WochenanzeigeKalenderwochen };
        kal.CheckedChanged += (_, _) => Lokal(() => e.WochenanzeigeKalenderwochen = kal.Checked);
        w.Controls.AddRange(new Control[] { schul, kal });
        _seite.Controls.Add(w);
        y += 74;
        _seite.Controls.Add(new Label
        {
            Text = "Die Versions-Nummer wird auf den Stundenplanausdrucken mitgedruckt (Berichte | Überschriften, „Stundenplan-Version drucken“).",
            Left = 12, Top = y, Width = 520, Height = 32, ForeColor = Color.DimGray,
        });
        y += 36;
    }

    private void LoggingSeiteAnmelden()
    {
        _baum.Nodes.Add("Logging und Crash Reporting");
        _seiten["Logging und Crash Reporting"] = SeiteLogging;
    }

    /// <summary>Logging und Crash Reporting: Absturzberichte und Wiederherstellungskopie (alles bleibt lokal).</summary>
    private void SeiteLogging()
    {
        var e = AppZustand.Einstellungen;
        var y = 8;
        var ber = Ui.Zeile(_seite, "Absturzberichte speichern", new CheckBox { Checked = e.AbsturzberichteSpeichern }, ref y, 300);
        ber.CheckedChanged += (_, _) => Lokal(() => e.AbsturzberichteSpeichern = ber.Checked);
        var kopie = Ui.Zeile(_seite, "Bei Absturz Wiederherstellungskopie der Daten anlegen", new CheckBox { Checked = e.AbsturzWiederherstellungskopie }, ref y, 300);
        kopie.CheckedChanged += (_, _) => Lokal(() => e.AbsturzWiederherstellungskopie = kopie.Checked);
        var hinweis = Ui.Zeile(_seite, "Beim Start auf ungewolltes Beenden hinweisen", new CheckBox { Checked = e.AbsturzHinweisBeimStart }, ref y, 300);
        hinweis.CheckedChanged += (_, _) => Lokal(() => e.AbsturzHinweisBeimStart = hinweis.Checked);
        var anz = Ui.Zeile(_seite, "Anzahl aufbewahrter Absturzberichte", new NumericUpDown { Minimum = 1, Maximum = 500, Value = Math.Clamp(e.AbsturzberichteBehalten, 1, 500) }, ref y, 300, 60);
        anz.ValueChanged += (_, _) => Lokal(() => e.AbsturzberichteBehalten = (int)anz.Value);
        var ordner = Absturzschutz.Sicherung.Berichtsordner;
        var zahl = Absturzschutz.Sicherung.Berichte().Count;
        var oeffnen = new Button { Text = "Ordner öffnen", Left = 12, Top = y + 6, Width = 120, Height = 26 };
        oeffnen.Click += (_, _) =>
        {
            Directory.CreateDirectory(ordner);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ordner) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { Ui.Fehler(ex.Message); }
        };
        var loeschen = new Button { Text = "Berichte löschen", Left = 140, Top = y + 6, Width = 120, Height = 26, Enabled = zahl > 0 };
        loeschen.Click += (_, _) => { Absturzschutz.Sicherung.Aufraeumen(0); loeschen.Enabled = false; };
        _seite.Controls.AddRange(new Control[] { oeffnen, loeschen });
        y += 40;
        _seite.Controls.Add(new Label
        {
            Text = $"{zahl} Absturzbericht(e) in {ordner}.\n\nEin Absturzbericht enthält Fehlermeldung, Version, Systemarchitektur, Sprache und Sitzungsdauer – " +
                   "keine personenbezogenen Daten. UntPlan überträgt weder Absturzberichte noch Daten zur Qualitätssicherung ins Internet; " +
                   "Berichte können bei Bedarf selbst an den Support weitergegeben werden.",
            Left = 12, Top = y, Width = 520, Height = 90, ForeColor = Color.DimGray,
        });
    }
}
