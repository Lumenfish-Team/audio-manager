using FMOD;
using FMOD.Studio;
using FMODUnity;

namespace Lumenfish.AudioManagement
{
    public readonly struct FmodEvent
    {
        /// <summary>
        /// Represents the file path of an FMOD event within the project.
        /// Used to locate and reference specific audio events.
        /// </summary>
        private readonly string _eventPath;
        
        public FmodEvent(string path) { _eventPath = path; }

        /// <summary>
        /// Plays a single instance of the associated FMOD audio event at the set event path.
        /// This method is intended for one-shot sounds that do not require additional control
        /// after being triggered, such as sound effects for UI interactions or environmental triggers.
        /// </summary>
        public void PlayOneShot() => RuntimeManager.PlayOneShot(_eventPath);

        /// Creates an instance of the associated FMOD event.
        /// <returns>
        /// Returns an instance of the FMOD.Studio.EventInstance class, which
        /// represents a runtime instance of the event that can be controlled
        /// and manipulated (e.g., starting, stopping, setting parameters).
        /// </returns>
        public EventInstance CreateInstance() => RuntimeManager.CreateInstance(_eventPath);
    }

    /// <summary>
    /// Represents a wrapper for an FMOD Bus, which provides functionality to manage and control
    /// audio categories or groups in the FMOD Studio audio system.
    /// </summary>
    /// <remarks>
    /// An FMOD Bus is typically used to apply volume controls, routing, or other operations
    /// to a set of related audio events. This struct allows for resolving and manipulating
    /// an FMOD Bus identified by a specific path.
    /// </remarks>
    public readonly struct FmodBus
    {
        /// <summary>
        /// Stores the path of the FMOD bus as a string.
        /// This path is used to reference and manipulate the bus within FMOD Studio's audio system.
        /// </summary>
        private readonly string _busPath;
        
        public FmodBus(string path) { _busPath = path; }
        
        public Bus Resolve() => RuntimeManager.GetBus(_busPath);
        
        public RESULT SetVolume(float volume)
        {
            var bus = Resolve();
            return bus.isValid() ? bus.setVolume(volume) : RESULT.ERR_INVALID_HANDLE;
        }
    }

    /// <summary>
    /// Provides access to FMOD sound events and buses within the application.
    /// This class acts as a centralized manager for retrieving FMOD event and bus objects
    /// based on their respective predefined identifiers.
    /// </summary>
    public static class SoundManager
    {
        /// <summary>
        /// Retrieves an FMOD event based on the specified event ID.
        /// </summary>
        /// <param name="id">The unique identifier of the FMOD event to retrieve.</param>
        /// <returns>The corresponding FMOD event as an FmodEvent object.</returns>
        public static FmodEvent GetEvent(FmodEventId id) => new(FmodEventDatabase.GetPath(id));

        /// <summary>
        /// Retrieves an FMOD bus instance associated with the specified <see cref="FmodBusId"/>.
        /// </summary>
        /// <param name="id">The identifier representing the FMOD bus to retrieve.</param>
        /// <returns>A <see cref="FmodBus"/> instance corresponding to the specified bus id.</returns>
        public static FmodBus GetBus(FmodBusId id) => new(FmodBusDatabase.GetPath(id));
    }
}