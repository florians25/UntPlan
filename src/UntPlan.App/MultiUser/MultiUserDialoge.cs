using UntPlan.App.Dialoge;
using UntPlan.Core.MultiUser;

namespace UntPlan.App.MultiUser;

/// <summary>Passwort vergeben oder ändern (neues Passwort mit Wiederholung).</summary>
public static class PasswortDialog
{
    /// <summary>Liefert (altes, neues) Passwort oder null bei Abbruch. Ein leeres neues Passwort löscht das Passwort.</summary>
    public static (string Alt, string Neu)? Zeigen(IWin32Window owner, string benutzer, bool altesAbfragen, string hinweis = "")
    {
        using var d = new Dialog($"Passwort für {benutzer}", 380, altesAbfragen ? 190 : 164);
        var y = 12;
        if (hinweis.Length > 0)
        {
            d.Controls.Add(new Label { Text = hinweis, Left = 12, Top = y, Width = 356, Height = 34 });
            y += 38;
        }
        var alt = altesAbfragen ? Ui.Zeile(d, "Bisheriges Passwort", new TextBox { UseSystemPasswordChar = true }, ref y, 150, 200) : null;
        var neu = Ui.Zeile(d, "Neues Passwort", new TextBox { UseSystemPasswordChar = true }, ref y, 150, 200);
        var wdh = Ui.Zeile(d, "Wiederholung", new TextBox { UseSystemPasswordChar = true }, ref y, 150, 200);
        d.FormClosing += (_, e) =>
        {
            if (d.DialogResult != DialogResult.OK || neu.Text == wdh.Text) return;
            Ui.Info("Die Passwörter stimmen nicht überein.");
            e.Cancel = true;
        };
        return d.ShowDialog(owner) == DialogResult.OK ? (alt?.Text ?? "", neu.Text) : null;
    }
}

/// <summary>Zwei Eingabefelder, z. B. Nummer und Bezeichnung einer neuen Schule.</summary>
public static class ZweiFelderDialog
{
    public static (string A, string B)? Zeigen(IWin32Window owner, string titel, string textA, string vorgabeA, string textB, string vorgabeB)
    {
        using var d = new Dialog(titel, 380, 120);
        var y = 14;
        var a = Ui.Zeile(d, textA, new TextBox { Text = vorgabeA }, ref y, 120, 230);
        var b = Ui.Zeile(d, textB, new TextBox { Text = vorgabeB }, ref y, 120, 230);
        return d.ShowDialog(owner) == DialogResult.OK ? (a.Text.Trim(), b.Text.Trim()) : null;
    }
}

/// <summary>Zielversion wählen: Schule, Schuljahr, Versionsnummer (fehlende Schulen/Schuljahre werden angelegt).</summary>
public static class ZielVersionDialog
{
    public sealed record Ziel(string Schule, string Schulname, string Schuljahr, int Version, string Bezeichnung);

    public static Ziel? Zeigen(IWin32Window owner, MultiUserDb db, string titel, string schule, string schulname, string schuljahr, int version)
    {
        using var d = new Dialog(titel, 420, 220);
        var y = 14;
        var cbSchule = Ui.Zeile(d, "Schulnummer", new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Text = schule }, ref y, 130, 260);
        var name = Ui.Zeile(d, "Schulname", new TextBox { Text = schulname }, ref y, 130, 260);
        var cbJahr = Ui.Zeile(d, "Schuljahr (JJJJ/JJJJ)", new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Text = schuljahr }, ref y, 130, 260);
        var nr = Ui.Zeile(d, "Version", new NumericUpDown { Minimum = 1, Maximum = 999, Value = Math.Max(1, version) }, ref y, 130, 80);
        var bez = Ui.Zeile(d, "Bezeichnung", new TextBox { Text = "" }, ref y, 130, 260);
        var schulen = db.Schulen();
        foreach (var s in schulen) cbSchule.Items.Add(s.Nr);
        void JahreLaden()
        {
            cbJahr.Items.Clear();
            var s = schulen.FirstOrDefault(x => x.Nr == cbSchule.Text.Trim());
            if (s == null) return;
            if (name.Text.Length == 0) name.Text = s.Bezeichnung;
            foreach (var j in db.Schuljahre(s.Id)) cbJahr.Items.Add(j.Nr);
        }
        cbSchule.TextChanged += (_, _) => JahreLaden();
        JahreLaden();
        cbSchule.Text = schule;
        cbJahr.Text = schuljahr;
        if (d.ShowDialog(owner) != DialogResult.OK) return null;
        return new Ziel(cbSchule.Text.Trim(), name.Text.Trim(), cbJahr.Text.Trim(), (int)nr.Value, bez.Text.Trim());
    }

    /// <summary>Sucht bzw. legt Schule und Schuljahr an und liefert die Schuljahr-Id.</summary>
    public static SchuljahrInfo SchuljahrSichern(MultiUserDb db, string schule, string schulname, string schuljahr)
    {
        var s = db.Schulen().FirstOrDefault(x => string.Equals(x.Nr, schule, StringComparison.OrdinalIgnoreCase)) ?? db.SchuleAnlegen(schule, schulname);
        var nr = schuljahr.Replace('-', '/');
        return db.Schuljahre(s.Id).FirstOrDefault(x => x.Nr == nr) ?? db.SchuljahrAnlegen(s.Id, nr, "");
    }

    /// <summary>Schuljahr im Format JJJJ/JJJJ zu einem Datum (Wechsel am 1. August).</summary>
    public static string SchuljahrZu(DateOnly datum)
    {
        var j = datum.Month >= 8 ? datum.Year : datum.Year - 1;
        return $"{j}/{j + 1}";
    }
}
