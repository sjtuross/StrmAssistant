using HarmonyLib;
using MediaBrowser.Controller.Entities;
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
        private static MethodInfo _getPersonFromXmlNode;

        private static readonly AsyncLocal<string> PersonContent = new AsyncLocal<string>();

        private static readonly XmlReaderSettings ReaderSettings = new XmlReaderSettings
        {
            ValidationType = ValidationType.None,
            Async = true,
            CheckCharacters = false,
            IgnoreProcessingInstructions = true,
            IgnoreComments = true
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
                    var genericBaseNfoParserPerson = genericBaseNfoParser.MakeGenericType(typeof(Person));
                    _getPersonFromXmlNode = genericBaseNfoParserPerson.GetMethod("GetPersonFromXmlNode",
                        BindingFlags.NonPublic | BindingFlags.Instance);
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
                    if (!IsPatched(_getPersonFromXmlNode, typeof(EnhanceNfoMetadata)))
                    {
                        HarmonyMod.Patch(_getPersonFromXmlNode,
                            prefix: new HarmonyMethod(typeof(EnhanceNfoMetadata).GetMethod("GetPersonFromXmlNodePrefix",
                                BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(EnhanceNfoMetadata).GetMethod(
                                "GetPersonFromXmlNodePostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                        Plugin.Instance.logger.Debug("Patch GetPersonFromXmlNode Success by Harmony");
                    }
                }
                catch (Exception he)
                {
                    Plugin.Instance.logger.Debug("Patch EnhanceNfoMetadata Failed by Harmony");
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
                    if (IsPatched(_getPersonFromXmlNode, typeof(EnhanceNfoMetadata)))
                    {
                        HarmonyMod.Unpatch(_getPersonFromXmlNode,
                            AccessTools.Method(typeof(EnhanceNfoMetadata), "GetPersonFromXmlNodePrefix"));
                        HarmonyMod.Unpatch(_getPersonFromXmlNode,
                            AccessTools.Method(typeof(EnhanceNfoMetadata), "GetPersonFromXmlNodePostfix"));
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
        
        [HarmonyPostfix]
        private static bool GetPersonFromXmlNodePrefix(ref XmlReader reader)
        {
            var sb = new StringBuilder();
    
            using (var writer = new StringWriter(sb))
            {
                using (var xmlWriter = XmlWriter.Create(writer))
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

            return true;
        }

        [HarmonyPostfix]
        private static void GetPersonFromXmlNodePostfix(XmlReader reader, Task<PersonInfo> __result)
        {
            Task.Run(async () => await SetImageUrlAsync(__result));
        }

        private static async Task SetImageUrlAsync(Task<PersonInfo> personInfoTask)
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
                            }

                            break;
                        }
                    }
                }
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
