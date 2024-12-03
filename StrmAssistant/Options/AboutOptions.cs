using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements.List;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System.ComponentModel;

namespace StrmAssistant
{
    public class AboutOptions : EditableOptionsBase
    {
        public AboutOptions()
        {
            VersionInfoList = new GenericItemList();
        }

        [DisplayNameL("AboutOptions_EditorTitle_About", typeof(Resources))]
        public override string EditorTitle => Resources.AboutOptions_EditorTitle_About;

        public GenericItemList VersionInfoList { get; set; }

        [Browsable(false)]
        public string GitHubToken { get; set; } = string.Empty;
    
        [Browsable(false)]
        public string GitHubProxy { get; set; } = string.Empty;
    }
}