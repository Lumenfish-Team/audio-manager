using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Lumenfish.AudioManagement
{
    /// <summary>
    /// The SoundInitializer class is responsible for loading FMOD audio banks
    /// into the application at runtime.
    /// </summary>
    /// <remarks>
    /// This class allows users to specify a list of FMOD banks to load and ensures
    /// that all sample data is fully loaded before proceeding.
    /// </remarks>
    public class SoundInitializer : MonoBehaviour
    {
        /// <summary>
        /// A list of FMOD Studio bank assets used to initialize sound banks.
        /// This variable holds the names of FMOD Studio banks that can be used
        /// within the project to manage and load sound data.
        /// </summary>
        [FMODUnity.BankRef]
        public List<string> banks;

        /// Loads FMOD sound banks asynchronously into memory.
        /// This method iterates through a collection of FMOD sound bank names
        /// and loads them into memory using the FMOD Runtime Manager. It then waits
        /// for all sample data associated with the loaded banks to complete loading before execution continues.
        /// <returns>
        /// An IEnumerator that can be used to manage the asynchronous loading
        /// process through Unity's coroutine system.
        /// </returns>
        public IEnumerator LoadSounds()
        {
            foreach (var bank in banks)
            {
                FMODUnity.RuntimeManager.LoadBank(bank, true);
            }
            
            while (FMODUnity.RuntimeManager.AnySampleDataLoading())
            {
                yield return null;
            }
        }
    }
}
