using UnityEngine;

namespace Versus.Remaster
{
    // Free-fly navigation for a stage scene (Scenes/Stages/<stg>.unity), in Play mode. It flies the scene's
    // camera anywhere over and through the stage (no collision).
    //   WASD move   Q/E down/up   Shift faster   Ctrl slower   right-drag look   scroll speed
    //   R back to the start view   H hide help
    public sealed class StageNavigator : MonoBehaviour
    {
        [Tooltip("Flying speed in metres per second (scroll changes it in Play).")]
        public float speed = 8;
        [Tooltip("Mouse look speed (degrees per mouse unit).")]
        public float lookSpeed = 3;

        Camera view;
        Vector3 startPosition;
        float yaw, pitch, startYaw, startPitch;
        bool showHelp = true;

        void Start()
        {
            view = Camera.main;
            if (!view) { view = new GameObject("Fly camera").AddComponent<Camera>(); view.farClipPlane = 2000; }
            // Start where the scene's camera stands: behind player one's side, looking at the match start.
            startPosition = view.transform.position;
            var e = view.transform.eulerAngles;
            startYaw = yaw = e.y; startPitch = pitch = e.x > 180 ? e.x - 360 : e.x;
        }

        void Update()
        {
            if (!view) return;
            if (Input.GetKeyDown(KeyCode.H)) showHelp = !showHelp;
            if (Input.GetKeyDown(KeyCode.R)) { view.transform.position = startPosition; yaw = startYaw; pitch = startPitch; }
            if (Input.GetMouseButton(1)) { yaw += Input.GetAxis("Mouse X") * lookSpeed; pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSpeed, -89, 89); }
            speed = Mathf.Clamp(speed * Mathf.Pow(1.15f, Input.mouseScrollDelta.y), .5f, 200);

            var look = Quaternion.Euler(pitch, yaw, 0);
            var move = new Vector3((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0), 0,
                                   (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            float up = (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);
            float factor = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 4 :
                           Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ? .25f : 1;
            view.transform.rotation = look;
            view.transform.position += (look * Vector3.ClampMagnitude(move, 1) + Vector3.up * up) * speed * factor * Time.deltaTime;
        }

        void OnGUI()
        {
            if (!showHelp || !view) return;
            GUI.Box(new Rect(10, 10, 450, 58), "");
            GUI.Label(new Rect(18, 14, 440, 54),
                "FLY  WASD move • Q/E down/up • Shift faster • Ctrl slower • right-drag look\n" +
                "scroll speed (" + speed.ToString("0.#") + " m/s) • R start view • H hide help\n" +
                "Position " + view.transform.position.ToString("0.0"));
        }
    }
}
