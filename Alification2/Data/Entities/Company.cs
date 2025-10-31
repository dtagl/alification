
namespace Alification.Data.Entities;

public class Company
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string PasswordHash { get; set; }
    public DateTime WorkingStart { get; set; }
    public int WorkingHours { get; set; }
    public ICollection<User> Users { get; set; }
    public ICollection<Room> Rooms { get; set; }
    public ICollection<Booking> Bookings { get; set; }

}