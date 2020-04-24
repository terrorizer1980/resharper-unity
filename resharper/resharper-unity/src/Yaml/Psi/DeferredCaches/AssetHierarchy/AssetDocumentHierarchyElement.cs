using System;
using System.Collections;
using System.Collections.Generic;
using JetBrains.Diagnostics;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.AssetHierarchy.Elements;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.AssetHierarchy.Elements.Prefabs;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.AssetHierarchy.Elements.Stripped;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.Interning;
using JetBrains.ReSharper.Psi;
using JetBrains.Util;

namespace JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.AssetHierarchy
{
    public partial class AssetDocumentHierarchyElement : IUnityAssetDataElement
    {
        
        private readonly List<IHierarchyElement> myOtherElements = new List<IHierarchyElement>();
        
        // avoid boxing
        private readonly List<TransformHierarchy> myTransformElements = new List<TransformHierarchy>();
        private readonly List<ScriptComponentHierarchy> myScriptComponentElements = new List<ScriptComponentHierarchy>();
        private readonly List<ComponentHierarchy> myComponentElements = new List<ComponentHierarchy>();
        private readonly List<GameObjectHierarchy> myGameObjectHierarchies = new List<GameObjectHierarchy>();

        private readonly Dictionary<ulong, int> myGameObjectLocationToTransform = new Dictionary<ulong, int>(); 
        

        private readonly List<int> myPrefabInstanceHierarchies = new List<int>();


        public bool IsScene { get; internal set; }

        public AssetDocumentHierarchyElementContainer AssetDocumentHierarchyElementContainer { get; internal set; }
        
        public AssetDocumentHierarchyElement(IPsiSourceFile sourceFile) : this(sourceFile.PsiStorage.PersistentIndex)
        {
        }

        private AssetDocumentHierarchyElement(long ownerId)
        {
            OwnerId = ownerId;
        }

        public long OwnerId { get; }
        public string ContainerId => nameof(AssetDocumentHierarchyElementContainer);
        public void AddData(object data)
        {
            if (data == null)
                return;
            
            // avoid boxing
            if (data is GameObjectHierarchy gameObjectHierarchy)
                myGameObjectHierarchies.Add(gameObjectHierarchy);
            else if (data is ScriptComponentHierarchy scriptComponentHierarchy)
                myScriptComponentElements.Add(scriptComponentHierarchy);
            else if (data is TransformHierarchy transformHierarchy)
                myTransformElements.Add(transformHierarchy);
            else if (data is ComponentHierarchy componentHierarchy)
                myComponentElements.Add(componentHierarchy);
            else 
                myOtherElements.Add(data as IHierarchyElement);
        }

        public IHierarchyElement GetHierarchyElement(string ownerGuid, ulong anchor, UnityInterningCache unityInterningCache, PrefabImportCache prefabImportCache)
        {
            var result = SearchForAnchor(unityInterningCache, anchor);
            if (result != null)
            {
                if (!(result is IStrippedHierarchyElement) || prefabImportCache == null) // stipped means, that element is not real and we should import prefab
                    return result;
            }
            
            // In prefabs files, anchor for stripped element is always generated by formula in PrefabsUtil. This means
            // that after we import elements from prefab file into another file, we could reuse anchor from stripped element to
            // get real element (in current implementation, imported objects are store in PrefabImportCache)
            // It is not true(!!!) for scene files, anchors for scene files could be generated by sequence generator. This means,
            // that anchor for stripped element could be '19' for example, but imported element will have another anchor.
            //
            // To unify all logic, if ownerGuid is related to scene file and achor points to stripped file, we will 
            // use new anchor which calculated in same way with prefab import  
            if (result != null && IsScene && result is IStrippedHierarchyElement strippedHierarchyElement )
            {
                var prefabInstance = strippedHierarchyElement.GetPrefabInstance(unityInterningCache);
                var correspondingObject = strippedHierarchyElement.GetCoresspondingSourceObject(unityInterningCache);
                if (prefabInstance != null && correspondingObject != null)
                    anchor = PrefabsUtil.Import(prefabInstance.LocalDocumentAnchor, correspondingObject.LocalDocumentAnchor);
            }

            if (prefabImportCache != null)
            {
                var elements = prefabImportCache.GetImportedElementsFor(unityInterningCache, ownerGuid, this);
                
                if (elements.TryGetValue(anchor, out var importedResult))
                    return importedResult;
            }
            
            return null;
        }

        // boxing is not problem here
        private IHierarchyElement SearchForAnchor(UnityInterningCache unityInterningCache, ulong anchor)
        {
            return
                SearchForAnchor(myGameObjectHierarchies, unityInterningCache, anchor) ??
                SearchForAnchor(myTransformElements, unityInterningCache, anchor) ??
                SearchForAnchor(myScriptComponentElements, unityInterningCache, anchor) ??
                SearchForAnchor(myComponentElements, unityInterningCache, anchor) ??
                SearchForAnchor(myOtherElements, unityInterningCache, anchor);
        }


        private IHierarchyElement SearchForAnchor<T>(List<T> elements, UnityInterningCache cache, ulong anchor) where T : IHierarchyElement
        {
            var searchResult = elements.BinarySearchEx(a => a.GetLocation(cache).LocalDocumentAnchor.CompareTo(anchor));
            if (searchResult.IsHit)
                return searchResult.HitItem;

            return null;
        }
        

        public IEnumerable<IPrefabInstanceHierarchy> GetPrefabInstanceHierarchies()
        {
            for (int i = 0; i < myPrefabInstanceHierarchies.Count; i++)
            {
                var element = GetElementByInternalIndex(myPrefabInstanceHierarchies[i]);
                if (element != null)
                    Assertion.Assert(element is IPrefabInstanceHierarchy, "element is IPrefabInstanceHierarchy");
                yield return element as IPrefabInstanceHierarchy;
            }
        }

        private readonly object myLockObject = new object();
        private volatile bool myIsRestored = false;
        public void RestoreHierarchy(AssetDocumentHierarchyElementContainer hierarchyElementContainer,
            IPsiSourceFile sourceFile,
            UnityInterningCache unityInterningCache)
        {
            if (myIsRestored)
                return;

            AssetDocumentHierarchyElementContainer = hierarchyElementContainer;
            IsScene = sourceFile.GetLocation().ExtensionWithDot.Equals(UnityYamlConstants.Scene);

            lock (myLockObject)
            {
                if (myIsRestored)
                    return;
                
                myIsRestored = true;
 
                var offset = 0;
                // concating arrays to one by index. see GetElementByInternalIndex too
                FillIndices(myOtherElements, offset, unityInterningCache);
                offset += myOtherElements.Count;
                
                FillIndices(myTransformElements, offset, unityInterningCache);
                offset += myTransformElements.Count;

                FillIndices(myGameObjectHierarchies, offset, unityInterningCache);
                offset += myGameObjectHierarchies.Count;

                FillIndices(myComponentElements, offset, unityInterningCache);
                offset += myComponentElements.Count;
                
                FillIndices(myScriptComponentElements, offset, unityInterningCache);
                offset += myScriptComponentElements.Count;

            }
        }


        private void FillIndices<T>(List<T> list, int curOffset, UnityInterningCache unityInterningCache) where  T : IHierarchyElement
        {
            list.Sort((a, b) => a.GetLocation(unityInterningCache).LocalDocumentAnchor.
                CompareTo(b.GetLocation(unityInterningCache).LocalDocumentAnchor));
            
            for (int i = 0; i < list.Count; i++)
            {
                var element = list[i];
                if (element is ITransformHierarchy transformHierarchy)
                {
                    var reference = transformHierarchy.GetOwner(unityInterningCache);
                    if (reference != null)
                    {
                        myGameObjectLocationToTransform[reference.LocalDocumentAnchor] = curOffset + i;
                    }
                }

                if (element is IPrefabInstanceHierarchy prefabInstanceHierarchy)
                    myPrefabInstanceHierarchies.Add(curOffset + i);
            }
        }

        internal ITransformHierarchy GetTransformHierarchy(UnityInterningCache cache, GameObjectHierarchy gameObjectHierarchy)
        {
            var transformIndex = myGameObjectLocationToTransform.GetValueSafe(gameObjectHierarchy.GetLocation(cache).LocalDocumentAnchor, -1);
            if (transformIndex == -1)
                return null;

            var element = GetElementByInternalIndex(transformIndex);
            if (element != null)
                Assertion.Assert(element is ITransformHierarchy, "element is ITransformHierarchy");
            
            return element as ITransformHierarchy;
        }

        private IHierarchyElement GetElementByInternalIndex(int index)
        {
            if (index < myOtherElements.Count)
                return myOtherElements[index];

            index -= myOtherElements.Count;
            
            if (index < myTransformElements.Count)
                return myTransformElements[index];

            index -= myTransformElements.Count;
            
            if (index < myGameObjectHierarchies.Count)
                return myGameObjectHierarchies[index];

            index -= myGameObjectHierarchies.Count;
            
            if (index < myComponentElements.Count)
                return myComponentElements[index];

            index -= myComponentElements.Count;
            
            if (index < myScriptComponentElements.Count)
                return myScriptComponentElements[index];

            index -= myScriptComponentElements.Count;
            
            
            throw new IndexOutOfRangeException("Index was out of range in concated array");
        }

        public IEnumerable<IHierarchyElement> Elements()
        {
            foreach (var otherElement in myOtherElements)
                yield return otherElement;
            
            foreach (var otherElement in myTransformElements)
                yield return otherElement;
            
            foreach (var otherElement in myGameObjectHierarchies)
                yield return otherElement;
            
            foreach (var otherElement in myComponentElements)
                yield return otherElement;
            
            foreach (var otherElement in myScriptComponentElements)
                yield return otherElement;
        }
    }
}