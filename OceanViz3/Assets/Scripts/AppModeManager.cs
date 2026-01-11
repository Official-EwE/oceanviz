using UnityEngine;

namespace OceanViz3
{
    public abstract class AppModeManager : MonoBehaviour
    {
        protected MainScene mainScene;

        public virtual void Setup(MainScene mainScene)
        {
            this.mainScene = mainScene;
        }

        // Called when this mode becomes active
        public abstract void EnterMode();

        // Called when this mode becomes inactive
        public abstract void ExitMode();

        // Called when the main menu is opened
        public virtual void EnterMenu() {}

        // Called when the main menu is closed
        public virtual void ExitMenu() {}

        // Called every frame by MainScene's Update
        public virtual void OnUpdate() {}
        
        // Called by MainScene when a new location has finished loading
        public virtual void OnLocationReady() {}

        // Called when the Escape key is pressed. Return true if the event was handled.
        public virtual bool OnEscapePressed() { return false; }

		// Called when the global HUDless state changes. Default implementation does nothing.
		public virtual void SetHudless(bool hudless) {}
    }
} 