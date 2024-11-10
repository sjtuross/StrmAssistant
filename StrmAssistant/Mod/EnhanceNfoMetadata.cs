using HarmonyLib;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using static StrmAssistant.Mod.PatchManager;

namespace StrmAssistant.Mod
{
    public static class EnhanceNfoMetadata
    {
        private static readonly PatchApproachTracker PatchApproachTracker = new PatchApproachTracker();
        
        private static Assembly _nfoMetadataAssembly;
        private static ConstructorInfo _genericBaseNfoParserConstructor;
        private static MethodInfo _getPersonFromXmlNode;
        
        private static MethodInfo _getPersonFromXmlNodePrefix;
        private static MethodInfo _getPersonFromXmlNodePostfix;

        private static readonly AsyncLocal<string> PersonContent = new AsyncLocal<string>();

        private static readonly XmlReaderSettings ReaderSettings = new XmlReaderSettings
        {
            ValidationType = ValidationType.None,
            Async = true,
            CheckCharacters = false,
            IgnoreProcessingInstructions = true,
            IgnoreComments = true
        };

        private static readonly XmlWriterSettings WriterSettings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            CheckCharacters = false
        };

        public static void Initialize()
        {
            try
            {
                _nfoMetadataAssembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "NfoMetadata");

                if (_nfoMetadataAssembly != null)
                {
                    var genericBaseNfoParser = _nfoMetadataAssembly.GetType("NfoMetadata.Parsers.BaseNfoParser`1");
                    var genericBaseNfoParserVideo = genericBaseNfoParser.MakeGenericType(typeof(Video));
                    _genericBaseNfoParserConstructor = genericBaseNfoParserVideo.GetConstructor(BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new Type[]
                        {
                            typeof(ILogger), typeof(IConfigurationManager), typeof(IProviderManager),
                            typeof(IFileSystem)
                        }, null);
                    _getPersonFromXmlNode = genericBaseNfoParserVideo.GetMethod("GetPersonFromXmlNode",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    _getPersonFromXmlNodePrefix = typeof(EnhanceNfoMetadata).GetMethod("GetPersonFromXmlNodePrefix",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    _getPersonFromXmlNodePostfix = typeof(EnhanceNfoMetadata).GetMethod("GetPersonFromXmlNodePostfix",
                        BindingFlags.Static | BindingFlags.NonPublic);
                }
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Warn("EnhanceNfoMetadata - Patch Init Failed");
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
                PatchApproachTracker.FallbackPatchApproach = PatchApproach.None;
            }

            if (HarmonyMod == null) PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;

            if (PatchApproachTracker.FallbackPatchApproach != PatchApproach.None &&
                Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.EnhanceNfoMetadata)
            {
                Patch();
            }
        }

        public static void Patch()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony && _nfoMetadataAssembly != null)
            {
                try
                {
                    if (!IsPatched(_genericBaseNfoParserConstructor, typeof(EnhanceNfoMetadata)))
                    {
                        HarmonyMod.Patch(_genericBaseNfoParserConstructor,
                            prefix: new HarmonyMethod(typeof(EnhanceNfoMetadata).GetMethod("GenericBaseNfoParserConstructorPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug("Patch GenericBaseNfoParserConstructor Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch GenericBaseNfoParserConstructor Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                    PatchApproachTracker.FallbackPatchApproach = PatchApproach.Reflection;
                }
            }
        }

        public static void Unpatch()
        {
            if (PatchApproachTracker.FallbackPatchApproach == PatchApproach.Harmony)
            {
                try
                {
                    if (IsPatched(_genericBaseNfoParserConstructor, typeof(EnhanceNfoMetadata)))
                    {
                        HarmonyMod.Unpatch(_genericBaseNfoParserConstructor,
                            AccessTools.Method(typeof(EnhanceNfoMetadata), "GenericBaseNfoParserConstructorPrefix"));
                        Plugin.Instance.logger.Debug("Unpatch GenericBaseNfoParserConstructor Success by Harmony");
                    }
                    if (IsPatched(_getPersonFromXmlNode, typeof(EnhanceNfoMetadata)))
                    {
                        HarmonyMod.Unpatch(_getPersonFromXmlNode, _getPersonFromXmlNodePostfix);
                        HarmonyMod.Unpatch(_getPersonFromXmlNode, _getPersonFromXmlNodePostfix);
                        Plugin.Instance.logger.Debug("Unpatch GetPersonFromXmlNode Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Unpatch EnhanceNfoMetadata Failed by Harmony");
                    Plugin.Instance.logger.Debug(he.Message);
                    Plugin.Instance.logger.Debug(he.StackTrace);
                }
            }
        }

        [HarmonyPrefix]
        private static bool GenericBaseNfoParserConstructorPrefix(object __instance)
        {
            try
            {
                HarmonyMod.Unpatch(_getPersonFromXmlNode, _getPersonFromXmlNodePrefix);
                HarmonyMod.Unpatch(_getPersonFromXmlNode, _getPersonFromXmlNodePostfix);
                HarmonyMod.Patch(_getPersonFromXmlNode, prefix: new HarmonyMethod(_getPersonFromXmlNodePrefix),
                    postfix: new HarmonyMethod(_getPersonFromXmlNodePostfix));
            }
            catch (Exception he)
            {
                Plugin.Instance.logger.Debug("Patch GetPersonFromXmlNode Failed by Harmony");
                Plugin.Instance.logger.Debug(he.Message);
                Plugin.Instance.logger.Debug(he.StackTrace);
            }

            return true;
        }
        
        [HarmonyPrefix]
        private static bool GetPersonFromXmlNodePrefix(ref XmlReader reader)
        {
            try
            {
                var sb = new StringBuilder();
    
                using (var writer = new StringWriter(sb))
                {
                    using (var xmlWriter = XmlWriter.Create(writer, WriterSettings))
                    {
                        while (reader.Read())
                        {
                            xmlWriter.WriteNode(reader, true);

                            if (reader.NodeType == XmlNodeType.EndElement)
                                break;
                        }
                    }
                }

                PersonContent.Value = sb.ToString();

                reader = XmlReader.Create(new StringReader(sb.ToString()), ReaderSettings);
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
            }
            
            return true;
        }

        [HarmonyPostfix]
        private static void GetPersonFromXmlNodePostfix(XmlReader reader, Task<PersonInfo> __result)
        {
            Task.Run(async () => await SetImageUrlAsync(__result)).ConfigureAwait(false);
        }

        private static async Task SetImageUrlAsync(Task<PersonInfo> personInfoTask)
        {
            try
            {
                var personInfo = await personInfoTask;

                var personContent = PersonContent.Value;
                PersonContent.Value = null;

                if (personContent != null)
                {
                    using (var reader = XmlReader.Create(new StringReader(personContent), ReaderSettings))
                    {
                        while (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            if (reader.IsStartElement("thumb"))
                            {
                                var thumb = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);

                                if (IsValidHttpUrl(thumb))
                                {
                                    personInfo.ImageUrl = thumb;
                                    //Plugin.Instance.logger.Debug("EnhanceNfoMetadata - Imported " + personInfo.Name +
                                    //                             " " + personInfo.ImageUrl);
                                }

                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Instance.logger.Debug(e.Message);
                Plugin.Instance.logger.Debug(e.StackTrace);
            }
        }

        private static bool IsValidHttpUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uriResult))
            {
                return uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps;
            }

            return false;
        }
    }
}
