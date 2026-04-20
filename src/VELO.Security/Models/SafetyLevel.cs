namespace VELO.Security.Models;

public enum SafetyLevel
{
    Red,        // Threat blocked or critical TLS failure
    Yellow,     // Warning — suspicious signals
    Green,      // No threats detected
    Gold,       // Golden List: privacy-excellent domain
    Analyzing,  // Transient: AI/CT analysis in progress
}
