

namespace Alification.Data.Entities;

public class Room
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public int Capacity { get; set; }
    public string Description { get; set; }
    public Guid CompanyId { get; set; }
    public Company Company { get; set; }
    // 96-slot availability is computed per date, not stored in DB
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool[] Times { get; set; } = new bool[96];
    public ICollection<Booking> Bookings { get; set; }
}