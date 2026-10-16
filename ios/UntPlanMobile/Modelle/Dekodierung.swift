import Foundation

/// Nachsichtige Dekodierhilfen: fehlende, leere oder anders getypte Felder führen nicht zum Abbruch.
extension KeyedDecodingContainer {
    /// Beliebiger Typ, bei Fehlern nil.
    func optional<T: Decodable>(_ key: Key) -> T? {
        try? decodeIfPresent(T.self, forKey: key)
    }

    /// Text, auch wenn der Server eine Zahl oder einen Wahrheitswert liefert.
    func textOderNil(_ key: Key) -> String? {
        if let s = try? decodeIfPresent(String.self, forKey: key) { return s }
        if let i = try? decodeIfPresent(Int.self, forKey: key) { return String(i) }
        if let d = try? decodeIfPresent(Double.self, forKey: key) { return String(d) }
        if let b = try? decodeIfPresent(Bool.self, forKey: key) { return b ? "true" : "false" }
        return nil
    }

    func text(_ key: Key) -> String {
        textOderNil(key) ?? ""
    }

    func zahl(_ key: Key) -> Int? {
        if let i = try? decodeIfPresent(Int.self, forKey: key) { return i }
        if let d = try? decodeIfPresent(Double.self, forKey: key) { return Int(d) }
        if let s = try? decodeIfPresent(String.self, forKey: key) { return Int(s) }
        return nil
    }

    func wahr(_ key: Key) -> Bool {
        if let b = try? decodeIfPresent(Bool.self, forKey: key) { return b }
        if let i = try? decodeIfPresent(Int.self, forKey: key) { return i != 0 }
        if let s = try? decodeIfPresent(String.self, forKey: key) { return s == "true" || s == "1" }
        return false
    }

    /// Liste; fehlt sie oder ist sie unlesbar, dann leer. Unlesbare einzelne Einträge werden übersprungen,
    /// statt die ganze Liste zu verwerfen.
    func liste<T: Decodable>(_ key: Key) -> [T] {
        if let l = try? decodeIfPresent([T].self, forKey: key) { return l }
        let einzeln = (try? decodeIfPresent([Vielleicht<T>].self, forKey: key)) ?? []
        return einzeln.compactMap(\.wert)
    }

    /// Liste von Texten (Zahlen werden zu Text).
    func texte(_ key: Key) -> [String] {
        if let l = try? decodeIfPresent([String].self, forKey: key) { return l }
        if let l = try? decodeIfPresent([Int].self, forKey: key) { return l.map { String($0) } }
        return []
    }
}

/// Hülle für einen einzelnen Listeneintrag: unlesbar wird zu nil statt zum Fehler.
struct Vielleicht<T: Decodable>: Decodable {
    let wert: T?

    init(from decoder: Decoder) throws {
        wert = try? T(from: decoder)
    }
}
