using UnityEngine;
namespace OTK.Midterm
{
    public sealed class MidtermDummyVisual : MonoBehaviour
    {
        public Transform leftLeg, rightLeg;
        private Vector2 movement;
        private float phase;
        public void SetMovementInput(Vector2 input) { movement=input; }
        private void Update()
        {
            bool walking=movement.sqrMagnitude>.001f;
            if(walking)
            {
                var facing=Quaternion.LookRotation(new Vector3(movement.x,0,movement.y));
                transform.rotation=Quaternion.RotateTowards(transform.rotation,facing,540*Time.deltaTime);
                phase+=Time.deltaTime*11;
            }
            float angle=walking?Mathf.Sin(phase)*24:0;
            leftLeg.localRotation=Quaternion.Euler(angle,0,0);
            rightLeg.localRotation=Quaternion.Euler(-angle,0,0);
        }
    }
}
