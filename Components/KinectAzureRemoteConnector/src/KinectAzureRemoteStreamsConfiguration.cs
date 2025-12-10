using Microsoft.Psi.Remoting;

namespace SAAC.RemoteConnectors
{ 
    // Why not use the psi azure configuration as base?
    public class KinectAzureRemoteStreamsConfiguration : Microsoft.Psi.AzureKinect.AzureKinectSensorConfiguration
    {
        public int KinectDeviceIndex { get; set; } = 0;

        // Stream available
        public bool OutputAudio { get; set; } = true;
        public bool OutputBodies { get; set; } = true;

        // Configuration for video stream
        public int EncodingVideoLevel { get; set; } = 90;

        // Network
        public TransportKind ConnectionType { get; set; } = TransportKind.Tcp;
        public string IpToUse { get; set; } = "localhost";
        public int StartingPort { get; set; } = 11411;
        public string RendezVousApplicationName { get; set; } = "RemoteKinectAzureServer";
    }
}
