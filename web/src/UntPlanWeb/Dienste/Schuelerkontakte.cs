using System.IO.Compression;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Kontakt einer Schülerin/eines Schülers (Erziehungsberechtigte*r bzw. Ausbildungsbeauftragte*r eines Betriebs/einer Einrichtung).</summary>
public sealed record Schuelerkontakt(int Id, int SchuelerId, string Rolle, string Name, string Betrieb, string Telefon, string Mobil, string Email);

/// <summary>Eingabe eines Kontakts bzw. der eigenen Adresse.</summary>
public sealed record KontaktEingabe(int? Id, string? Rolle, string? Name, string? Betrieb, string? Telefon, string? Mobil, string? Email);

/// <summary>
/// Kontaktdaten und Bilder der Schüler*innen (Leitfaden Klassenleitung, Kapitel 5 und 6): eigene Adresse (E-Mail, Telefon, Mobiltelefon),
/// Erziehungsberechtigte und Ausbildungsbeauftragte mit E-Mail-Adresse (Grundlage der Selbstregistrierung), Bild je Schüler*in
/// (einzeln hochladen, als ZIP-Archiv „Nachname, Vorname.jpg“ importieren oder im eigenen Profil). Einsehen und bearbeiten dürfen die
/// Klassenleitung, die Schulleitung und die Administration (Schülerverwaltung); ihr eigenes Bild laden Schüler*innen selbst hoch.
/// </summary>
public sealed class Schuelerkontakte
{
    public const string Eigene = "eigene", Erziehungsberechtigt = "erziehungsberechtigt", Ausbildungsbeauftragt = "ausbildungsbeauftragt";
    public static readonly string[] Personenrollen = { Erziehungsberechtigt, Ausbildungsbeauftragt };
    /// <summary>Höchstgröße eines Bildes (Bytes).</summary>
    public const int MaxBild = 2 * 1024 * 1024;
    private static readonly Dictionary<string, string> Bildtypen = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif", [".webp"] = "image/webp",
    };

    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Paketdienst _pakete;
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer De = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true);

    public Schuelerkontakte(Db db, Uhr uhr, Paketdienst pakete) { _db = db; _uhr = uhr; _pakete = pakete; }

    private string Jetzt => _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm");

    private static WebSchueler SchuelerVon(Stand s, int id) => s.Schueler.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Unbekannte Schülerin / unbekannter Schüler.");

    private static void Bearbeiten(Benutzer b, Stand s, WebSchueler x)
    {
        if (!Schuelerverwaltung.DarfBearbeiten(b, s, x.Klasse)) throw new UnauthorizedAccessException("Kontaktdaten sehen und bearbeiten die Klassenleitung, die Schulleitung und die Administration.");
    }

    private static string Feld(string? t, int max, string name)
    {
        var v = (t ?? "").Trim();
        if (v.Length > max) throw new ArgumentException($"„{name}“ ist zu lang.");
        return v;
    }

    private static string Mail(string? t)
    {
        var v = Feld(t, 200, "E-Mail-Adresse");
        if (v.Length > 0 && !Maildienst.Gueltig(v)) throw new ArgumentException($"„{v}“ ist keine gültige E-Mail-Adresse.");
        return v;
    }

    // ------------------------------------------------------------------------------------------ Kontaktdaten

    public List<Schuelerkontakt> Kontakte(int schuelerId) => _db.Liste("SELECT * FROM schueler_kontakte WHERE schueler_id = @s ORDER BY rolle DESC, id",
        r => new Schuelerkontakt(r.Zahl("id"), r.Zahl("schueler_id"), r.Text("rolle"), r.Text("name"), r.Text("betrieb"), r.Text("telefon"), r.Text("mobil"), r.Text("email")),
        Db.P("@s", schuelerId));

    /// <summary>Kontaktdaten: Geburtsdatum, eigene Adresse, Erziehungsberechtigte und Ausbildungsbeauftragte.</summary>
    public object Kontaktdaten(Benutzer b, Stand s, int schuelerId)
    {
        var x = SchuelerVon(s, schuelerId);
        Bearbeiten(b, s, x);
        var d = _db.Liste("SELECT geburtsdatum, email, telefon, mobil FROM schueler_daten WHERE id = @id",
            r => (G: r.Text("geburtsdatum"), E: r.Text("email"), T: r.Text("telefon"), M: r.Text("mobil")), Db.P("@id", schuelerId)).FirstOrDefault();
        return new
        {
            schueler = new { x.Id, x.Name, x.Vorname, x.Klasse, geburtsdatum = d.G ?? "", email = d.E ?? "", telefon = d.T ?? "", mobil = d.M ?? "", bild = HatBild(schuelerId) },
            kontakte = Kontakte(schuelerId),
            // Erziehungsberechtigte aus „Personen & Konten“ (z. B. aus dem Schülerimport) – dort gepflegt
            personen = Erziehungsberechtigte().Where(p => p.Kinder.Contains(schuelerId)).Select(p => new { p.Id, p.Name, p.Telefon, p.Email }).ToList(),
            bearbeiten = true,
        };
    }

    /// <summary>Erziehungsberechtigte der Personenliste (Administration → Personen &amp; Konten, Schülerimport) mit ihren Kindern.</summary>
    public List<(int Id, string Name, string Telefon, string Email, List<int> Kinder)> Erziehungsberechtigte() =>
        _db.Liste("SELECT * FROM personen WHERE art = 'eltern'", r => (Id: r.Zahl("id"), Name: $"{r.Text("vorname")} {r.Text("name")}".Trim(),
            Telefon: Spalte(r, "telefon"), Email: r.Text("email"),
            Kinder: r.Text("kinder").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => int.TryParse(t, out var i) ? i : 0).Where(i => i > 0).ToList()));

    private static string Spalte(System.Data.Common.DbDataReader r, string name)
    {
        for (var i = 0; i < r.FieldCount; i++) if (r.GetName(i).Equals(name, StringComparison.OrdinalIgnoreCase)) return r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i)) ?? "";
        return "";
    }

    /// <summary>Eigene Adresse (E-Mail, Telefon, Mobiltelefon) speichern – überlagert die Schülerliste aus UntPlan wie die Schülerverwaltung.</summary>
    public void AdresseSpeichern(Benutzer b, Stand s, int schuelerId, string? email, string? telefon, string? mobil)
    {
        var x = SchuelerVon(s, schuelerId);
        Bearbeiten(b, s, x);
        var p = new[] { Db.P("@id", schuelerId), Db.P("@v", x.Vorname), Db.P("@n", x.Name), Db.P("@k", x.Klasse), Db.P("@e", Mail(email)), Db.P("@t", Feld(telefon, 50, "Telefonnummer")),
            Db.P("@m", Feld(mobil, 50, "Mobiltelefon")), Db.P("@z", Jetzt), Db.P("@u", b.Id), Db.P("@ei", schuelerId >= Schuelerverwaltung.ErsteEigeneId) };
        if (_db.Ausfuehren("UPDATE schueler_daten SET email = @e, telefon = @t, mobil = @m, geaendert = @z, benutzer_id = @u WHERE id = @id", p) == 0)
            _db.Ausfuehren("""
                INSERT INTO schueler_daten (id, vorname, name, klasse, email, telefon, mobil, eigen, geloescht, geaendert, benutzer_id)
                VALUES (@id, @v, @n, @k, @e, @t, @m, @ei, 0, @z, @u)
                """, p);
        _pakete.SchuelerLaden();
    }

    /// <summary>Kontakt (Erziehungsberechtigte*r bzw. Ausbildungsbeauftragte*r) anlegen oder ändern.</summary>
    public Schuelerkontakt KontaktSpeichern(Benutzer b, Stand s, int schuelerId, KontaktEingabe e)
    {
        var x = SchuelerVon(s, schuelerId);
        Bearbeiten(b, s, x);
        var rolle = (e.Rolle ?? "").Trim().ToLowerInvariant();
        if (!Personenrollen.Contains(rolle)) throw new ArgumentException("Bitte die Personenrolle wählen.");
        var name = Feld(e.Name, 200, "Name");
        var betrieb = Feld(e.Betrieb, 200, "Betrieb/Einrichtung");
        if (name.Length == 0 && betrieb.Length == 0) throw new ArgumentException("Bitte einen Namen eingeben.");
        var p = new[] { Db.P("@s", schuelerId), Db.P("@r", rolle), Db.P("@n", name), Db.P("@b", betrieb), Db.P("@t", Feld(e.Telefon, 50, "Telefonnummer")),
            Db.P("@m", Feld(e.Mobil, 50, "Mobiltelefon")), Db.P("@e", Mail(e.Email)), Db.P("@z", Jetzt), Db.P("@u", b.Id), Db.P("@id", e.Id ?? 0) };
        if (e.Id is int id && id > 0)
        {
            if (_db.Ausfuehren("UPDATE schueler_kontakte SET rolle = @r, name = @n, betrieb = @b, telefon = @t, mobil = @m, email = @e, geaendert = @z, benutzer_id = @u WHERE id = @id AND schueler_id = @s", p) == 0)
                throw new ArgumentException("Unbekannter Kontakt.");
        }
        else
            id = (int)_db.Einfuegen("INSERT INTO schueler_kontakte (schueler_id, rolle, name, betrieb, telefon, mobil, email, geaendert, benutzer_id) VALUES (@s, @r, @n, @b, @t, @m, @e, @z, @u)", p);
        return Kontakte(schuelerId).First(k => k.Id == id);
    }

    public void KontaktLoeschen(Benutzer b, Stand s, int schuelerId, int id)
    {
        Bearbeiten(b, s, SchuelerVon(s, schuelerId));
        _db.Ausfuehren("DELETE FROM schueler_kontakte WHERE id = @id AND schueler_id = @s", Db.P("@id", id), Db.P("@s", schuelerId));
    }

    // ------------------------------------------------------------------------------------------ Bilder

    public bool HatBild(int schuelerId) => Convert.ToInt32(_db.Wert("SELECT COUNT(*) FROM schueler_bilder WHERE schueler_id = @s", Db.P("@s", schuelerId))) > 0;

    /// <summary>Ids der Schüler*innen mit Bild (für Sitzplan, Abwesenheitskontrolle, Schülerliste).</summary>
    public HashSet<int> MitBild() => _db.Liste("SELECT schueler_id FROM schueler_bilder", r => r.Zahl("schueler_id")).ToHashSet();

    /// <summary>Darf der Benutzer das Bild sehen? Kollegium; Schüler- und Elternkonten nur das eigene bzw. das ihrer Kinder.</summary>
    public static bool DarfSehen(Benutzer b, int schuelerId) => b.Kollegium || b.SchuelerIds.Contains(schuelerId);

    public (byte[] Daten, string Typ)? Bild(Benutzer b, int schuelerId)
    {
        if (!DarfSehen(b, schuelerId)) throw new UnauthorizedAccessException("Dieses Bild dürfen Sie nicht sehen.");
        var l = _db.Liste("SELECT typ, daten FROM schueler_bilder WHERE schueler_id = @s", r => (T: r.Text("typ"), D: (byte[])r["daten"]), Db.P("@s", schuelerId));
        return l.Count == 0 ? null : (l[0].D, l[0].T);
    }

    private static string Typ(byte[] daten, string? name)
    {
        // Typ an den ersten Bytes erkennen (JPEG, PNG, GIF, WebP), sonst an der Endung
        if (daten.Length > 3 && daten[0] == 0xFF && daten[1] == 0xD8) return "image/jpeg";
        if (daten.Length > 8 && daten[0] == 0x89 && daten[1] == 0x50 && daten[2] == 0x4E && daten[3] == 0x47) return "image/png";
        if (daten.Length > 6 && daten[0] == 'G' && daten[1] == 'I' && daten[2] == 'F') return "image/gif";
        if (daten.Length > 12 && daten[0] == 'R' && daten[1] == 'I' && daten[2] == 'F' && daten[3] == 'F' && daten[8] == 'W' && daten[9] == 'E') return "image/webp";
        throw new ArgumentException($"„{name ?? "Datei"}“ ist kein Bild (JPG, PNG, GIF oder WebP).");
    }

    private void BildSchreiben(int schuelerId, byte[] daten, string typ, int benutzerId)
    {
        var p = new[] { Db.P("@s", schuelerId), Db.P("@t", typ), Db.P("@d", daten), Db.P("@z", Jetzt), Db.P("@u", benutzerId) };
        if (_db.Ausfuehren("UPDATE schueler_bilder SET typ = @t, daten = @d, geaendert = @z, benutzer_id = @u WHERE schueler_id = @s", p) == 0)
            _db.Ausfuehren("INSERT INTO schueler_bilder (schueler_id, typ, daten, geaendert, benutzer_id) VALUES (@s, @t, @d, @z, @u)", p);
    }

    /// <summary>Darf der Benutzer das Bild ändern? Klassenleitung/Schülerverwaltung bzw. die Schülerin/der Schüler selbst (eigenes Profil).</summary>
    public static bool DarfAendern(Benutzer b, Stand s, WebSchueler x) =>
        Schuelerverwaltung.DarfBearbeiten(b, s, x.Klasse) || (b.Rolle == Rollen.Schueler && b.SchuelerIds.Contains(x.Id));

    public void BildHochladen(Benutzer b, Stand s, int schuelerId, byte[] daten, string? name)
    {
        var x = SchuelerVon(s, schuelerId);
        if (!DarfAendern(b, s, x)) throw new UnauthorizedAccessException("Bilder laden die Klassenleitung bzw. die Schülerin/der Schüler selbst hoch.");
        if (daten.Length == 0) throw new ArgumentException("Bitte eine Bilddatei wählen.");
        if (daten.Length > MaxBild) throw new ArgumentException("Das Bild ist zu groß (höchstens 2 MB).");
        BildSchreiben(schuelerId, daten, Typ(daten, name), b.Id);
    }

    public void BildLoeschen(Benutzer b, Stand s, int schuelerId)
    {
        var x = SchuelerVon(s, schuelerId);
        if (!DarfAendern(b, s, x)) throw new UnauthorizedAccessException("Bilder ändern die Klassenleitung bzw. die Schülerin/der Schüler selbst.");
        _db.Ausfuehren("DELETE FROM schueler_bilder WHERE schueler_id = @s", Db.P("@s", schuelerId));
    }

    /// <summary>Name für den Abgleich im Bildimport: „Nachname, Vorname“ ohne Groß-/Kleinschreibung und doppelte Leerzeichen.</summary>
    private static string Schluessel(string t) => string.Join(" ", t.Replace(",", " , ").Split(' ', StringSplitOptions.RemoveEmptyEntries)).Replace(" , ", ", ").Trim().ToLowerInvariant();

    /// <summary>
    /// Bildimport: ZIP-Archiv mit Bildern „Nachname, Vorname.jpg“ (genau wie in der Schülerliste) für die Schüler*innen der Klasse.
    /// Liefert die Zahl der zugeordneten Bilder, nicht zuordenbare Dateien und Schüler*innen ohne Bild.
    /// </summary>
    public object Bildimport(Benutzer b, Stand s, string klasse, byte[] zip)
    {
        if (!s.Klassen.ContainsKey(klasse)) throw new ArgumentException($"Unbekannte Klasse {klasse}.");
        if (!Schuelerverwaltung.DarfBearbeiten(b, s, klasse)) throw new UnauthorizedAccessException("Den Bildimport nimmt die Klassenleitung bzw. die Administration vor.");
        var schueler = Klassenzugehoerigkeit.Am(s, new[] { klasse }, _uhr.Heute);
        var nachName = schueler.GroupBy(x => Schluessel($"{x.Name}, {x.Vorname}")).ToDictionary(g => g.Key, g => g.ToList());
        var zugeordnet = new HashSet<int>();
        var fehler = new List<string>();
        try
        {
            using var archiv = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            foreach (var eintrag in archiv.Entries.Where(e => e.Length > 0 && !e.FullName.EndsWith('/')))
            {
                var datei = Path.GetFileName(eintrag.FullName);
                if (datei.StartsWith('.') || eintrag.FullName.StartsWith("__MACOSX", StringComparison.Ordinal)) continue;
                var ext = Path.GetExtension(datei);
                if (!Bildtypen.ContainsKey(ext)) { fehler.Add($"{datei}: kein Bild"); continue; }
                if (eintrag.Length > MaxBild) { fehler.Add($"{datei}: zu groß (höchstens 2 MB)"); continue; }
                var name = Schluessel(Path.GetFileNameWithoutExtension(datei));
                if (!nachName.TryGetValue(name, out var treffer)) { fehler.Add($"{datei}: keine Schülerin/kein Schüler „{Path.GetFileNameWithoutExtension(datei)}“ in der Klasse {klasse}"); continue; }
                if (treffer.Count > 1) { fehler.Add($"{datei}: Name in der Klasse mehrfach vorhanden"); continue; }
                using var ms = new MemoryStream();
                using (var st = eintrag.Open()) st.CopyTo(ms);
                var daten = ms.ToArray();
                string typ;
                try { typ = Typ(daten, datei); } catch (ArgumentException ex) { fehler.Add(ex.Message); continue; }
                BildSchreiben(treffer[0].Id, daten, typ, b.Id);
                zugeordnet.Add(treffer[0].Id);
            }
        }
        catch (InvalidDataException) { throw new ArgumentException("Die Datei ist kein ZIP-Archiv."); }
        var mitBild = MitBild();
        return new
        {
            importiert = zugeordnet.Count, fehler,
            ohneBild = schueler.Where(x => !mitBild.Contains(x.Id)).Select(x => $"{x.Name}, {x.Vorname}").OrderBy(x => x, De).ToList(),
        };
    }
}
