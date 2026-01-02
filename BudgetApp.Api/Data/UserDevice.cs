using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Api.Data;

[Table("UserDevices")]
public class UserDevice
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Required]
    [Column("expo_push_token")]
    public string ExpoPushToken { get; set; } = null!;

    [Column("platform")]
    public string? Platform { get; set; } // "ios" | "android" | "web"

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey("UserId")]
    public User User { get; set; } = null!;
}
