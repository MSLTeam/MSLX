using Avalonia.Media.Imaging;

namespace MSLX.Desktop.Models;

public class MemberModel : System.ComponentModel.INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Desc { get; set; } = "";
    public string AvatarUrl { get; set; } = "";

    private Bitmap? _avatarBitmap;
    public Bitmap? AvatarBitmap
    {
        get => _avatarBitmap;
        set
        {
            _avatarBitmap = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(AvatarBitmap)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public class UpdateLogModel
{
    public string Version { get; set; } = "";
    public string Time { get; set; } = "";
    public string Changes { get; set; } = "";
}
