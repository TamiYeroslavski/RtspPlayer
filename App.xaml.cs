using System.Windows;
using LibVLCSharp.Shared;

namespace RtspPlayer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // אתחול LibVLC בתחילת האתחול (מונע בעיות "לא מוצא libvlc" בפרסום)
            Core.Initialize();
        }
    }
}
