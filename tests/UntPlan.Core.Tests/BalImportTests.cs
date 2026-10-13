using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using Xunit;

namespace UntPlan.Core.Tests;

public class BalImportTests
{
    private const string Beispiel = """
---------- WILLI2-Stundenplan ---------- Version: 460 ----------
"W","Testschule","Berufskolleg Test","X","PRIV"
"T1","Mo","Mo","VVVVVVNNXX",".........."
"T2","Di","Di","VVVVVVNNXX",".........."
"S1","1","1","8:00","8:45"
"S2","2","2","8:45","9:30"
"S3","3","3","9:45","10:30"
"S4","4","4","10:30","11:15"
"S5","5","5","11:30","12:15"
"S6","6","6","12:15","13:00"
"S7","7","7","13:15","14:00"
"S8","8","8","14:00","14:45"
"L1","NN","NN",,,,"0,000","0,000","0,000","0,000","0,000","0",,,"3","-1","0","1","0","0","0","5","3","N",,,,,"6","2",,"0,000","0,000","3",,"0,000","0,000"
"L2","AD","AD","Arold","Timo",,"0,000","0,000","25,500","0,000","0,000","0",,,"3","-1","0","1","0","0","0","5","3","N","Di,","1",,,"6","2",,"0,000","0,000","3","R1","0,000","0,000"
"L3","BD","BD","Barfeld","Theresa",,"0,000","0,000","0,000","0,000","0,000","0",,,"3","-1","0","1","0","0","0","5","3","N",,,,,"6","2",,"0,000","0,000","3",,"0,000","0,000"
"LQ3","BD","33333333333333","00000000000000"
"R1","R1","R1","Raum 1","33","0","255","0"
"R2","?","?",,"33","0","255","0"
"R3","R2","R2","Raum 2","33","0","255","0"
"F1","---","---",,,,"0","W"
"F2","D","D","Deutsch",,,"0","W"
"F3","E","E","Englisch",,,"0","W"
"F4","SP","SP","Sport",,,"0","W"
"K1","K1A","K1A",,,,,"0","32","16","0"
"K2","K1B","K1B",,,,,"0","32","16","0"
"O1","Hof","0"
"Z1","_Vor Unterricht","Vor","1","7:45","8:00"
"Z2","2//3","Vor","3","9:30","9:45"
"A1","Hof","2//3","AD","1","Vor","Mo 3"
"J1","SP_K1",,"12632256"
"U1","AD","D","K1A",,"4","4","4",,"32",,,,,,"R1","2","2","2","2","B"
"U2","BD","SP","K1A","SP_K1","2","2","2",,"32",,,,,,,"1","1","2","2","B"
"U3","AD","SP","K1B","SP_K1","2","2","2",,"32",,,,,,,"1","1","2","2","B"
"U4","BD","E","K1AB",,"1","1","1",,"32",,,,,,,"1","1","1","1","B"
"PL","Mo 1","AD","K1A","D","R1"
"PL","Mo 2","AD","K1A","D","R1"
"PL","Mo 3","BD","K1A","SP","R2"
"PL","Mo 3","AD","K1B","SP","R1"
"PL","Mo 4","BD","K1A","SP","R2"
"PL","Mo 4","AD","K1B","SP","R1"
"PL","Di 5","BD","K1AB","E","R2"
"MK","K1AB","K1A","K1B"
""";

    [Fact]
    public void ImportiertZeitrasterStammdatenKopplungUndPlan()
    {
        var d = Demo.NeueDatei("Leer", "BK");
        d.Lehrer.Clear(); d.Klassen.Clear(); d.Faecher.Clear(); d.Raeume.Clear();
        var b = BalImport.Importieren(d, Beispiel);

        Assert.Equal(2, d.Zeitraster.TageProWoche);
        Assert.Equal(8, d.Zeitraster.StundenProTag);
        Assert.Equal(Tageszeit.Nachmittag, d.Zeitraster.Art(0, 7));
        Assert.Equal(new TimeOnly(9, 45), d.Zeitraster.Zeit(3)!.Beginn);
        Assert.Contains(d.Zeitraster.Pausen, p => p.NachStunde == 2 && p.DauerMinuten == 15);

        Assert.DoesNotContain(d.Lehrer, l => l.Kurzname == "NN");
        Assert.DoesNotContain(d.Raeume, r => r.Kurzname == "?");
        var ad = d.Lehrer.Single(l => l.Kurzname == "AD");
        Assert.Equal("Arold", ad.Langname);
        Assert.Equal(25.5m, ad.Wochensoll);
        Assert.Equal(d.Raeume.Single(r => r.Kurzname == "R1").Id, ad.StammraumId);
        Assert.Equal(-2, ad.Zeitwuensche.Get(1, 3)); // freier Tag Di
        Assert.Equal(-3, d.Lehrer.Single(l => l.Kurzname == "BD").Zeitwuensche.Get(1, 1));

        // Kopplung über J-Gruppe: eine Unterrichtsnummer, zwei Zeilen
        var sport = d.Unterrichte.Single(u => u.Text == "SP_K1");
        Assert.Equal(2, sport.Zeilen.Count);
        // Blockung 2×2 → Doppelstunden
        var deutsch = d.Unterrichte.Single(u => u.Zeilen[0].FachId == d.Faecher.Single(f => f.Kurzname == "D").Id);
        Assert.Equal(new Bereich(2, 2), deutsch.Doppelstunden);
        // zusammengefasste Klasse K1AB → beide Klassen
        var englisch = d.Unterrichte.Single(u => u.Zeilen[0].FachId == d.Faecher.Single(f => f.Kurzname == "E").Id);
        Assert.Equal(2, englisch.Zeilen[0].KlassenIds.Count);

        // Stundenplan: gekoppelte Stunden ergeben eine Planstunde mit Raum je Zeile
        Assert.Equal(5, d.Stundenplan.Count);
        var mo3 = d.Stundenplan.Single(p => p.UnterrichtId == sport.Id && p.Tag == 0 && p.Stunde == 3);
        Assert.Equal(2, mo3.Raeume.Count(r => r.HasValue));
        Assert.Single(d.Pausenaufsichten);
        Assert.Equal(2, d.Pausenaufsichten[0].NachStunde);
        Assert.True(b.Neu > 0);
    }
}
