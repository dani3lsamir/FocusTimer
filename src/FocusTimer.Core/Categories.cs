namespace FocusTimer.Core;

/// <summary>Operaciones sobre la lista de categorías. Devuelven un mensaje de error en español, o null si fue bien.</summary>
public static class Categories
{
    public static CategoryDef? Find(AppSettings s, string id) => s.Categories.FirstOrDefault(c => c.Id == id);

    /// <summary>Busca la categoría actual por su nombre o por un nombre antiguo (alias).</summary>
    public static CategoryDef? Resolve(AppSettings s, string name)
    {
        name = (name ?? "").Trim();
        return s.Categories.FirstOrDefault(c => Same(c.Name, name))
            ?? s.Categories.FirstOrDefault(c => c.Aliases.Any(a => Same(a, name)));
    }

    /// <summary>Nombre con el que se agrupa una fila del CSV: el nombre actual de su categoría, o el original si ya no existe.</summary>
    public static string DisplayName(AppSettings s, string csvCategory) =>
        Resolve(s, csvCategory)?.Name ?? (string.IsNullOrWhiteSpace(csvCategory) ? "(no category)" : csvCategory.Trim());

    public static string? Add(AppSettings s, string name, out CategoryDef? added)
    {
        added = null;
        name = (name ?? "").Trim();
        if (Validate(s, name, null) is { } err) return err;
        added = new CategoryDef { Id = AppSettings.Categories_NewId(), Name = name };
        s.Categories.Add(added);
        return null;
    }

    /// <summary>Renombra sin tocar el CSV: el nombre anterior pasa a ser un alias.</summary>
    public static string? Rename(AppSettings s, string id, string newName)
    {
        var cat = Find(s, id);
        if (cat is null) return "Category not found.";
        newName = (newName ?? "").Trim();
        if (newName == cat.Name) return null;
        if (Validate(s, newName, cat) is { } err) return err;
        cat.Aliases.RemoveAll(a => Same(a, newName));
        if (!Same(cat.Name, newName) && !cat.Aliases.Any(a => Same(a, cat.Name))) cat.Aliases.Add(cat.Name);
        cat.Name = newName;
        return null;
    }

    /// <summary>Mueve la categoría delta posiciones (-1 sube, +1 baja).</summary>
    public static bool Move(AppSettings s, string id, int delta)
    {
        int i = s.Categories.FindIndex(c => c.Id == id);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= s.Categories.Count) return false;
        (s.Categories[i], s.Categories[j]) = (s.Categories[j], s.Categories[i]);
        return true;
    }

    private static string? Validate(AppSettings s, string name, CategoryDef? self)
    {
        if (name.Length == 0) return "Type a name.";
        if (name.Length > 40) return "40 characters max.";
        foreach (var c in s.Categories)
        {
            if (c == self) continue;
            if (Same(c.Name, name)) return "A category with that name already exists.";
            if (c.Aliases.Any(a => Same(a, name))) return $"That name was used before by \"{c.Name}\".";
        }
        return null;
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
