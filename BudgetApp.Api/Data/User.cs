using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Api.Data;

[Table("Users")]
public class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;
    [Column("email")]
    public string Email { get; set; } = string.Empty;

    [Column("firebase_uuid")]
    public string FirebaseUuid { get; set; } = string.Empty;

    [Column("createdAt")]
    public DateTime CreatedAt { get; set; }
    [Column("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public virtual ICollection<PlaidItem> PlaidItems { get; set; } = new List<PlaidItem>();
    public virtual ICollection<Balance> Balances { get; set; } = new List<Balance>();
}
