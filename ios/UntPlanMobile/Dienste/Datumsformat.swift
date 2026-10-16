import Foundation

/// Datumsangaben der API (yyyy-MM-dd) und deutsche Anzeige.
enum Datumsformat {
    static let kalender: Calendar = {
        var k = Calendar(identifier: .gregorian)
        k.locale = Locale(identifier: "de_DE")
        k.firstWeekday = 2
        k.minimumDaysInFirstWeek = 4
        return k
    }()

    private static let isoFormat: DateFormatter = {
        let f = DateFormatter()
        f.calendar = Calendar(identifier: .gregorian)
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd"
        return f
    }()

    private static let zeitFormat: DateFormatter = {
        let f = DateFormatter()
        f.calendar = Calendar(identifier: .gregorian)
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd HH:mm"
        return f
    }()

    static func iso(_ datum: Date) -> String {
        isoFormat.string(from: datum)
    }

    /// Liest "yyyy-MM-dd" (auch mit Uhrzeit dahinter).
    static func datum(_ text: String) -> Date? {
        let t = text.replacingOccurrences(of: "T", with: " ")
        if t.count >= 16, let d = zeitFormat.date(from: String(t.prefix(16))) { return d }
        return isoFormat.date(from: String(t.prefix(10)))
    }

    /// Deutsche Anzeige mit festem Muster, z. B. "EEEE, d. MMMM".
    static func anzeige(_ datum: Date, _ muster: String) -> String {
        let f = DateFormatter()
        f.calendar = kalender
        f.locale = Locale(identifier: "de_DE")
        f.dateFormat = muster
        return f.string(from: datum)
    }

    /// Anzeige eines API-Datums; unlesbare Werte bleiben unverändert.
    static func anzeige(_ text: String, _ muster: String) -> String {
        guard let d = datum(text) else { return text }
        return anzeige(d, muster)
    }

    static func montag(_ datum: Date) -> Date {
        let tag = kalender.startOfDay(for: datum)
        let wochentag = kalender.component(.weekday, from: tag) // 1 = Sonntag
        let abstand = (wochentag + 5) % 7
        return kalender.date(byAdding: .day, value: -abstand, to: tag) ?? tag
    }

    static func kalenderwoche(_ datum: Date) -> Int {
        kalender.component(.weekOfYear, from: datum)
    }

    static func tage(_ anzahl: Int, nach datum: Date) -> Date {
        kalender.date(byAdding: .day, value: anzahl, to: datum) ?? datum
    }

    static func istHeute(_ text: String) -> Bool {
        text.prefix(10) == iso(Date())
    }
}
