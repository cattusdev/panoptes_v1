namespace Panoptes.Constants
{
    public static class SystemRoles
    {
        // Ο Admin είναι superuser: περνάει όλα τα permission policies και ο ρόλος δεν αλλάζει ποτέ
        public const string Admin = "Admin";

        // Policy για το Admin area: Admin ή όποιος έχει έστω ένα permission
        public const string AdminAreaPolicy = "AdminArea";
    }
}
