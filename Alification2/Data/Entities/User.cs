namespace Alification.Data.Entities;

public class User
{
    public Guid Id { get; set; }
    public long TelegramId { get; set; }
    public string Name { get; set; }
    public Guid CompanyId { get; set; }
    public Role Role { get; set; }
    public Company Company { get; set; }
    public ICollection<Booking> Bookings { get; set; }
}