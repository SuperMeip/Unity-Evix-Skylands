using UnityEngine;

namespace Evix.Controllers.Unity {

  [RequireComponent(typeof(CharacterController))]
  public class PlayerController : MonoBehaviour {

    /// <summary>
    /// The model for the select block outline
    /// </summary>
    public GameObject selectedBlockOutlineObject;

    /// <summary>
    /// The character's head, for mouselook
    /// </summary>
    public GameObject headObject;

    /// <summary>
    /// The move speed of the player
    /// </summary>
    public float moveSpeed = 10;

    /// <summary>
    /// The mouselook clamp
    /// </summary>
    public Vector2 clampInDegrees = new Vector2(360, 180);

    /// <summary>
    /// Whether to lock cursor for mouselook
    /// </summary>
    public bool lockCursor;

    /// <summary>
    /// Sensitivity vector for mouselook
    /// </summary>
    public Vector2 sensitivity = new Vector2(2, 2);

    /// <summary>
    /// Smoothing vector for mouselook
    /// </summary>
    public Vector2 smoothing = new Vector2(3, 3);

    /// <summary>
    /// direction the camera is facing
    /// </summary>
    public Vector2 facingDirection;

    /// <summary>
    /// Direction the character is facing.
    /// </summary>
    public Vector2 targetCharacterDirection;

    /// <summary>
    /// The player
    /// </summary>
    public Player player {
      get;
      private set;
    }

    /// <summary>
    /// The character controller unity component, used for movement.
    /// </summary>
    CharacterController movementController;

    /// <summary>
    /// The absolute mouse mosition
    /// </summary>
    Vector2 mouseAbsolute;

    /// <summary>
    /// The smooth mouse position
    /// </summary>
    Vector2 smoothMouse;

    // Use this for initialization
    void Start() {
      movementController = GetComponent<CharacterController>();
      // Set target direction to the camera's initial orientation.
      facingDirection = headObject.transform.localRotation.eulerAngles;
      selectedBlockOutlineObject.transform.localScale = new Vector3(
        World.BlockSize + 0.01f,
        World.BlockSize + 0.01f,
        World.BlockSize + 0.01f
      );
    }

    // Update is called once per frame
    void Update() {
      move();
      look();
      udpdateCurrentlySelectedBlock();
    }

    /// <summary>
    /// Player movement management
    /// </summary>
    void move() {
      if (Input.GetAxis("Vertical") == 0 && Input.GetAxis("Horizontal") == 0) {
        return;
      }
      Vector3 fwd = headObject.transform.forward * Input.GetAxis("Vertical") * moveSpeed;
      Vector3 rgt = headObject.transform.right * Input.GetAxis("Horizontal") * moveSpeed;
      // get the total vector and check if we're moving
      Vector3 move = fwd + rgt;
      if (move.magnitude > 0) {
        // move character
        movementController.SimpleMove(move);
      }
    }

    /// <summary>
    /// Player mouselook management
    /// </summary>
    void look() {
      // Ensure the cursor is always locked when set
      if (lockCursor) {
        Cursor.lockState = CursorLockMode.Locked;
      }

      // Allow the script to clamp based on a desired target value.
      Quaternion targetOrientation = Quaternion.Euler(targetCharacterDirection);

      // Get raw mouse input for a cleaner reading on more sensitive mice.
      Vector2 mouseDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));

      // Scale input against the sensitivity setting and multiply that against the smoothing value.
      mouseDelta = Vector2.Scale(mouseDelta, new Vector2(sensitivity.x * smoothing.x, sensitivity.y * smoothing.y));

      // Interpolate mouse movement over time to apply smoothing delta.
      smoothMouse.x = Mathf.Lerp(smoothMouse.x, mouseDelta.x, 1f / smoothing.x);
      smoothMouse.y = Mathf.Lerp(smoothMouse.y, mouseDelta.y, 1f / smoothing.y);

      // Find the absolute mouse movement value from point zero.
      mouseAbsolute += smoothMouse;

      // Clamp and apply the local x value first, so as not to be affected by world transforms.
      if (clampInDegrees.x < 360) {
        mouseAbsolute.x = Mathf.Clamp(mouseAbsolute.x, -clampInDegrees.x * 0.5f, clampInDegrees.x * 0.5f);
      }

      // Then clamp and apply the global y value.
      if (clampInDegrees.y < 360) {
        mouseAbsolute.y = Mathf.Clamp(mouseAbsolute.y, -clampInDegrees.y * 0.5f, clampInDegrees.y * 0.5f);
      }

      // Set the new look rotations
      headObject.transform.localRotation = Quaternion.AngleAxis(-mouseAbsolute.y, targetOrientation * Vector3.right) * targetOrientation;
      Quaternion yRotation = Quaternion.AngleAxis(mouseAbsolute.x, headObject.transform.InverseTransformDirection(Vector3.up));
      headObject.transform.localRotation *= yRotation;
      facingDirection = headObject.transform.localRotation.eulerAngles;
    }

    /// <summary>
    /// Hilight the currently viewed block
    /// </summary>
    void udpdateCurrentlySelectedBlock() {
      Ray ray = Camera.main.ScreenPointToRay(new Vector3(Camera.main.pixelWidth / 2, Camera.main.pixelHeight / 2, 0));

      if (Physics.Raycast(ray, out RaycastHit hit, 25)) {
        Vector3 hitBlockPosition = hit.point + (hit.normal * -(World.BlockSize / 2));
        selectedBlockOutlineObject.transform.position = hitBlockPosition + new Vector3(World.BlockSize / 2, World.BlockSize / 2, World.BlockSize / 2);
        //removeBlockOnClick(hitBlockPosition);
      }
    }

    /// <summary>
    /// Remove a block on a button press
    /// </summary>
    /// <param name="hitBlock"></param>
    /*void removeBlockOnClick(Coordinate hitCoordinate) {
      if (Input.GetMouseButtonDown(0)) {
        ChunkController chunkController = player.level.chunkAtWorldLocation(hitCoordinate).controller;
        if (chunkController != null) {
          player.level.chunkAtWorldLocation(hitCoordinate).controller.destroyBlock(hitCoordinate);
        }
      }*/
  }
}