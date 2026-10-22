using UnityEngine;
namespace NarrativeGraybox.Interaction
{
    public static class GrayboxSolidObjects
    {
        public static void Apply()
        {
            foreach(var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if(renderer.GetComponentInParent<NarrativeGraybox.Player.IsometricPlayerController>()!=null)continue;
                if(renderer.sharedMaterial!=null && renderer.sharedMaterial.shader.name=="NarrativeGraybox/InspectionStroke")continue;
                var mesh=renderer.GetComponent<MeshFilter>();
                if(mesh==null||mesh.sharedMesh==null)continue;
                var collider=renderer.GetComponent<Collider>();
                if(collider==null)
                {
                    var box=renderer.gameObject.AddComponent<BoxCollider>();
                    box.center=mesh.sharedMesh.bounds.center;box.size=mesh.sharedMesh.bounds.size;
                    collider=box;
                }
                collider.isTrigger=false;
                collider.enabled=true;
            }
            // Inspection roots use a larger picking collider; make those solid too.
            foreach(var item in Object.FindObjectsByType<InspectionItem>(FindObjectsSortMode.None))
                foreach(var collider in item.GetComponents<Collider>())collider.isTrigger=false;
            Physics.SyncTransforms();
        }
    }
}
