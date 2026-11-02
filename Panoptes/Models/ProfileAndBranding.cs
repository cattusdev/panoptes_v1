using Panoptes.Models.Media;

namespace Panoptes.Models
{
    // Επιπλέον στοιχεία χρήστη (χωρίς αλλαγές στον πίνακα του Identity)
    public class UserProfile
    {
        public string UserId { get; set; } = "";
        public Guid? AvatarMediaId { get; set; }
        public MediaItem? Avatar { get; set; }
        public string? Language { get; set; } // null = η προεπιλογή της εφαρμογής (Settings → General)
    }

    // Η ταυτότητα της εφαρμογής: μία γραμμή (Id = 1)
    public class BrandingSettings
    {
        public const int SingletonId = 1;
        public const string DefaultAppName = "Panoptes";

        public int Id { get; set; } = SingletonId;
        public string AppName { get; set; } = DefaultAppName;
        public Guid? LogoMediaId { get; set; }
        public MediaItem? Logo { get; set; }
        public Guid? FaviconMediaId { get; set; }
        public MediaItem? Favicon { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
