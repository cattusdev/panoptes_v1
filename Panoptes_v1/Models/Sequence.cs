using Panoptes_v1.Models.Media;

namespace Panoptes_v1.Models
{
    // Ο επόμενος αριθμός κάθε σειράς κωδικών (π.χ. "CL" ή "CHG-2026"). Αλλάζει μόνο μέσα από τον ReferenceGenerator.
    public class Sequence : INotAudited
    {
        public string Name { get; set; } = "";
        public long NextValue { get; set; }
    }
}
