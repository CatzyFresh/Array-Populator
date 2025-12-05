using System.Collections.Generic;
using UnityEngine;

namespace CatzyFreshTool
{
    public class TestHarness : MonoBehaviour
    {
        [SerializeField] private GameObject[] gosArray;
        [SerializeField] private List<GameObject> gosList;

        [SerializeField] private MeshRenderer[] renderersArray;
        [SerializeField] private List<MeshRenderer> renderersList;

        [SerializeField] private ScriptableObject[] sosArray;
        [SerializeField] private List<ScriptableObject> sosList;

        [SerializeField] private AudioClip[] clipsArray;
        [SerializeField] private List<AudioClip> clipsList;
    }
}
