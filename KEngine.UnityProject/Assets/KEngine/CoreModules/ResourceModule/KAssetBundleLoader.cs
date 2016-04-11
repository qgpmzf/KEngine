﻿#region Copyright (c) 2015 KEngine / Kelly <http://github.com/mr-kelly>, All rights reserved.

// KEngine - Toolset and framework for Unity3D
// ===================================
// 
// Filename: KAssetBundleLoader.cs
// Date:     2015/12/03
// Author:  Kelly
// Email: 23110388@qq.com
// Github: https://github.com/mr-kelly/KEngine
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 3.0 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library.

#endregion

using System;
using System.Collections;
using System.IO;
using KEngine;
using UnityEngine;

namespace KEngine
{
    public enum KAssetBundleLoaderMode
    {
        Default,
        PersitentDataPathSync, // Use PersistentDataPath!
        StreamingAssetsWww, // default, use WWW class -> StreamingAssets Path
        ResourcesLoadAsync, // -> Resources path
        ResourcesLoad, // -> Resources Path
    }

    // 調用WWWLoader
    public class KAssetBundleLoader : KAbstractResourceLoader
    {
        public delegate void CAssetBundleLoaderDelegate(bool isOk, AssetBundle ab);

        public static Action<string> NewAssetBundleLoaderEvent;
        public static Action<KAssetBundleLoader> AssetBundlerLoaderErrorEvent;

        private KWWWLoader _wwwLoader;
        private KAssetBundleParser BundleParser;
        //private bool UnloadAllAssets; // Dispose时赋值
        public AssetBundle Bundle
        {
            get { return ResultObject as AssetBundle; }
        }

        private string RelativeResourceUrl;

        /// <summary>
        /// AssetBundle加载方式
        /// </summary>
        private KAssetBundleLoaderMode _loaderMode;

        /// <summary>
        /// AssetBundle读取原字节目录
        /// </summary>
        private KResourceInAppPathType _inAppPathType;

        public static KAssetBundleLoader Load(string url, CAssetBundleLoaderDelegate callback = null,
            KAssetBundleLoaderMode loaderMode = KAssetBundleLoaderMode.Default)
        {
            LoaderDelgate newCallback = null;
            if (callback != null)
            {
                newCallback = (isOk, obj) => callback(isOk, obj as AssetBundle);
            }
            var newLoader = AutoNew<KAssetBundleLoader>(url, newCallback, false, loaderMode);


            return newLoader;
        }

        protected override void Init(string url, params object[] args)
        {
            base.Init(url);

            _loaderMode = (KAssetBundleLoaderMode)args[0];

            // 如果是默认模式，则要判断ResourceModule.InAppPathType的默认为依据
            if (_loaderMode == KAssetBundleLoaderMode.Default)
            {
                _inAppPathType = KResourceModule.DefaultInAppPathType;
                switch (_inAppPathType)
                {
                    case KResourceInAppPathType.StreamingAssetsPath:
                        _loaderMode = KAssetBundleLoaderMode.StreamingAssetsWww;
                        break;
                    case KResourceInAppPathType.ResourcesAssetsPath:
                        _loaderMode = KAssetBundleLoaderMode.ResourcesLoad;
                        break;
                    case KResourceInAppPathType.PersistentAssetsPath:
                        _loaderMode = KAssetBundleLoaderMode.PersitentDataPathSync;
                        break;
                    default:
                        KLogger.LogError("Error DefaultInAppPathType: {0}", _inAppPathType);
                        break;
                }
            }
            // 不同的AssetBundle加载方式，对应不同的路径
            switch (_loaderMode)
            {
                case KAssetBundleLoaderMode.ResourcesLoad:
                case KAssetBundleLoaderMode.ResourcesLoadAsync:
                    _inAppPathType = KResourceInAppPathType.ResourcesAssetsPath;
                    break;
                case KAssetBundleLoaderMode.StreamingAssetsWww:
                    _inAppPathType = KResourceInAppPathType.StreamingAssetsPath;
                    break;
                case KAssetBundleLoaderMode.PersitentDataPathSync:
                    _inAppPathType = KResourceInAppPathType.PersistentAssetsPath;
                    break;
                default:
                    KLogger.LogError("[KAssetBundleLoader:Init]Unknow loader mode: {0}", _loaderMode);
                    break;
            }


            if (NewAssetBundleLoaderEvent != null)
                NewAssetBundleLoaderEvent(url);

            RelativeResourceUrl = url;
            KResourceModule.LogRequest("AssetBundle", RelativeResourceUrl);
            KResourceModule.Instance.StartCoroutine(LoadAssetBundle(url));
        }
        private IEnumerator LoadAssetBundle(string relativeUrl)
        {
            var bytesLoader = KBytesLoader.Load(relativeUrl, _inAppPathType, _loaderMode);
            while (!bytesLoader.IsCompleted)
            {
                yield return null;
            }
            if (!bytesLoader.IsSuccess)
            {
                if (AssetBundlerLoaderErrorEvent != null)
                {
                    AssetBundlerLoaderErrorEvent(this);
                }
                KLogger.LogError("[KAssetBundleLoader]Error Load Bytes AssetBundle: {0}", relativeUrl);
                OnFinish(null);
                yield break;
            }

            byte[] bundleBytes = bytesLoader.Bytes;
            Progress = 1 / 2f;
            bytesLoader.Release(); // 字节用完就释放

            BundleParser = new KAssetBundleParser(RelativeResourceUrl, bundleBytes);
            while (!BundleParser.IsFinished)
            {
                if (IsReadyDisposed) // 中途释放
                {
                    OnFinish(null);
                    yield break;
                }
                Progress = BundleParser.Progress / 2f + 1 / 2f; // 最多50%， 要算上WWWLoader的嘛
                yield return null;
            }

            Progress = 1f;
            var assetBundle = BundleParser.Bundle;
            if (assetBundle == null)
                KLogger.LogError("WWW.assetBundle is NULL: {0}", RelativeResourceUrl);

            OnFinish(assetBundle);

            //Array.Clear(cloneBytes, 0, cloneBytes.Length);  // 手工释放内存

            //GC.Collect(0);// 手工释放内存
        }

        protected override void OnFinish(object resultObj)
        {
            if (_wwwLoader != null)
            {
                // 释放WWW加载的字节。。释放该部分内存，因为AssetBundle已经自己有缓存了
                _wwwLoader.Release();
                _wwwLoader = null;
            }
            base.OnFinish(resultObj);
        }

        protected override void DoDispose()
        {
            base.DoDispose();

            if (BundleParser != null)
                BundleParser.Dispose(false);
        }

        public override void Release()
        {
            if (Application.isEditor)
            {
                if (Url.Contains("Arial"))
                {
                    KLogger.LogError("要释放Arial字体！！错啦！！builtinextra:{0}", Url);
                    //UnityEditor.EditorApplication.isPaused = true;
                }
            }

            base.Release();
        }

        /// 舊的tips~忽略
        /// 原以为，每次都通过getter取一次assetBundle会有序列化解压问题，会慢一点，后用AddWatch调试过，发现如果把.assetBundle放到Dictionary里缓存，查询会更慢
        /// 因为，估计.assetBundle是一个纯Getter，没有做序列化问题。  （不保证.mainAsset）
    }

}
