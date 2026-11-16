using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Fenster;

/// <summary>Handbuch Kap. 6/7.4.3: Text zu Stundenplanstunde, Wochenanzeige (Schul-/Kalenderwochen), Periodenwechsel.</summary>
public sealed partial class StundenplanFenster
{
    private int? _letztePeriode;

    /// <summary>Wochenbereich eines Unterrichts in der Lupe („6-27“ bzw. „KW 32-KW 6“) laut Einstellung „Wochenanzeige“.</summary>
    private string Wochen(DateOnly von, DateOnly bis) =>
        AppZustand.Einstellungen.WochenanzeigeKalenderwochen
            ? $"KW {System.Globalization.ISOWeek.GetWeekOfYear(von.ToDateTime(TimeOnly.MinValue))}-KW {System.Globalization.ISOWeek.GetWeekOfYear(bis.ToDateTime(TimeOnly.MinValue))}"
            : $"{Kalender.Schulwoche(D, von)}-{Kalender.Schulwoche(D, bis)}";

    /// <summary>
    /// „Bei Periodenwechsel Stundenplan auf Periodenbeginn stellen“ (Einstellungen | Diverses | Stundenplan): nach dem Wechsel der aktiven
    /// Periode springt das Datum auf deren Beginn.
    /// </summary>
    private void PeriodenwechselPruefen()
    {
        var aktiv = D.AktivePeriodeId;
        var vorher = _letztePeriode;
        _letztePeriode = aktiv;
        if (vorher is null || vorher == aktiv || !AppZustand.Einstellungen.PeriodenwechselPeriodenbeginn) return;
        var beginn = D.AktivePeriode.Von;
        if (beginn < D.Einstellungen.SchuljahrVon) beginn = D.Einstellungen.SchuljahrVon;
        AppZustand.DatumSetzen(beginn);
    }

    /// <summary>
    /// Handbuch 6.5.2.7 „Text zu Stundenplanstunde“: individueller Text nur für diese eine Stunde des Unterrichts (auch wenn er mehrstündig
    /// verplant ist). Anzeige über das Feld „Stundentext“ der Stundenplan-Stunde und in der Lupe (Sondertext).
    /// </summary>
    private void StundentextBearbeiten(Planstunde p)
    {
        var index = D.Stundenplan.IndexOf(p);
        if (index < 0) return;
        var text = Ui.Eingabe($"Text zu Stundenplanstunde {Planungsdienst.Tagname(D, p.Tag)}-{D.Zeitraster.StundenLabel(p.Stunde)} (U-Nr. {p.UnterrichtId}):", "Text zu Stundenplanstunde", p.Text);
        if (text is null || text == p.Text) return;
        Aendern("Text zu Stundenplanstunde", AenderungsArt.Stundenplan, d => { if (index < d.Stundenplan.Count) d.Stundenplan[index].Text = text.Trim(); });
        if (!_plan.Format.Felder.Any(f => f.Art == StundenFeldArt.Stundentext) && text.Trim().Length > 0)
            AppZustand.Haupt.StatusMelden("Hinweis: Damit der Text in der Stunde erscheint, das Feld „Stundentext“ in die Stundenplan-Stunde aufnehmen (Stundenplan-Einstellungen).");
    }

    /// <summary>
    /// Handbuch 6.8 „Verwendung der Zwischenablage“: der angezeigte Stundenplan wird als Grafik (und zusätzlich im HTML-Format) in die
    /// Zwischenablage kopiert und kann so z. B. in eine Textverarbeitung eingefügt werden.
    /// </summary>
    internal void AlsGrafikKopieren()
    {
        if (_plan.Width <= 0 || _plan.Height <= 0) return;
        try
        {
            using var bild = new Bitmap(_plan.Width, _plan.Height);
            _plan.DrawToBitmap(bild, new Rectangle(Point.Empty, _plan.Size));
            var data = new DataObject();
            data.SetData(DataFormats.Bitmap, true, new Bitmap(bild));
            var html = Core.Austausch.Export.StundenplanHtml(D, _typ, _id);
            data.SetData(DataFormats.Html, ClipboardHtml(html));
            Clipboard.SetDataObject(data, true);
            AppZustand.Haupt.StatusMelden("Stundenplan in die Zwischenablage kopiert (Grafik und HTML).");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException or ArgumentException) { Ui.Fehler(ex.Message); }
    }
}
