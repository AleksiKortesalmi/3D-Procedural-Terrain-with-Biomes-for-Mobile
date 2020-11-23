using UnityEngine;
using Utility;

public class CameraControl : MonoBehaviour
{
	public float sensitivity = 1;
	public float moveSpeed = 1;
	float xVelocity = 0, zVelocity = 0, height = 25;
	float rotationX = 0;
	float rotationY = 0;
	bool mouse0Down = false, mouse1Down = false;
	Quaternion startRotation;

	private void Awake()
	{
		if(Application.platform == RuntimePlatform.Android)
			Application.targetFrameRate = 60;
	}

	void Start()
	{
		startRotation = transform.localRotation;

		AccelerometerCalibration.CalibrateAccelerometer();

		Cursor.lockState = CursorLockMode.Locked;
	}

	void Update()
	{
		if (Application.platform == RuntimePlatform.Android)
		{
			if (Input.touchCount != 0)
			{
				transform.Translate(Vector3.forward * moveSpeed * Time.deltaTime * 120, Space.Self);
			}

			Vector3 fixedAcceleration = AccelerometerCalibration.FixAcceleration(Input.acceleration);

			transform.Rotate(new Vector3(fixedAcceleration.y * sensitivity * Time.deltaTime * 120, 0, 0), Space.Self);

			transform.Rotate(new Vector3(0, fixedAcceleration.x * sensitivity * Time.deltaTime   * 120, 0), Space.World);
		}
		else
		{
			// Movement
			if (Input.GetAxisRaw("Horizontal") != 0 && xVelocity < moveSpeed && xVelocity > -moveSpeed)
			{
				xVelocity += Input.GetAxisRaw("Horizontal") * moveSpeed / 30;
			}
			else if (xVelocity > 0.05f)
			{
				xVelocity -= moveSpeed / 60;
			}
			else if (xVelocity < -0.05f)
			{
				xVelocity += moveSpeed / 60;
			}
			else
			{
				xVelocity = 0;
			}

			if (Input.GetAxisRaw("Vertical") != 0 && zVelocity < moveSpeed && zVelocity > -moveSpeed)
			{
				zVelocity += Input.GetAxisRaw("Vertical") * moveSpeed / 30;
			}
			else if (zVelocity > 0.05f)
			{
				zVelocity -= moveSpeed / 60;
			}
			else if (zVelocity < -0.05f)
			{
				zVelocity += moveSpeed / 60;
			}
			else
			{
				zVelocity = 0;
			}
			transform.Translate(new Vector3(xVelocity, 0, zVelocity), Space.Self);

			// Height
			if (Input.GetMouseButtonDown(0))
			{
				mouse0Down = true;
			}
			else if (Input.GetMouseButtonUp(0))
			{
				mouse0Down = false;
			}

			if (Input.GetMouseButtonDown(1))
			{
				mouse1Down = true;
			}
			else if (Input.GetMouseButtonUp(1))
			{
				mouse1Down = false;
			}

			if (mouse0Down)
				height += moveSpeed;
			if (mouse1Down)
				height -= moveSpeed;

			transform.position = new Vector3(transform.position.x, height, transform.position.z);

			//Rotation
			rotationY += Input.GetAxis("Mouse Y") * sensitivity;
			rotationX += Input.GetAxis("Mouse X") * sensitivity;

			Quaternion yQuaternion = Quaternion.AngleAxis(rotationY, Vector3.left);
			Quaternion xQuaternion = Quaternion.AngleAxis(rotationX, Vector3.up);

			transform.localRotation = startRotation * xQuaternion * yQuaternion;
		}
	}
}

public static class AccelerometerCalibration
{
	public static Quaternion calibrationQuaternion;

	public static void CalibrateAccelerometer()
	{
		Vector3 accelerationSnapshot = Input.acceleration;
		Quaternion rotateQuaternion = Quaternion.FromToRotation(new Vector3(0.0f, 0.0f, -1.0f), accelerationSnapshot);
		calibrationQuaternion = Quaternion.Inverse(rotateQuaternion);
	}

	//Get the 'calibrated' value from the Input
	public static Vector3 FixAcceleration(Vector3 acceleration)
	{
		Vector3 fixedAcceleration = calibrationQuaternion * acceleration;
		return fixedAcceleration;
	}
}