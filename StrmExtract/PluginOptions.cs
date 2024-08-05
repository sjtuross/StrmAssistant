using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Validation;
using System.ComponentModel;

namespace StrmExtract
{
    public class PluginOptions : EditableOptionsBase
    {
        public override string EditorTitle => "Strm Extract";

        //public override string EditorDescription => "Strm Extract Description";

        [Description("MaxConcurrentCount must be between 1 to 10. Default is 1.")]
        [MediaBrowser.Model.Attributes.Required]
        public int MaxConcurrentCount { get; set; } = 1;

        protected override void Validate(ValidationContext context)
        {
            if (this.MaxConcurrentCount<1 || this.MaxConcurrentCount>10)
            {
                context.AddValidationError(nameof(this.MaxConcurrentCount), "MaxConcurrentCount must be between 1 to 10");
            }
        }
    }
}