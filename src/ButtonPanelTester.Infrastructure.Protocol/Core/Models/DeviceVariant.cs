namespace Core.Models;

/// <summary>
/// Variante del device. Rimpiazza i simboli di preprocessore
/// <c>#if TOPLIFT/EDEN/EGICON</c> con un valore a runtime letto da
/// <c>appsettings.json</c>.
/// </summary>
public enum DeviceVariant
{
    /// <summary>Configurazione generica (nessun device specifico).</summary>
    Generic,
    /// <summary>Lettino TOPLIFT A2.</summary>
    TopLift,
    /// <summary>Lettino EDEN XP.</summary>
    Eden,
    /// <summary>Display EGICON (SPARK).</summary>
    Egicon
}
