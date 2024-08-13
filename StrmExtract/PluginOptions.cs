using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Validation;
using System.ComponentModel;

namespace StrmExtract
{
    public class PluginOptions: EditableOptionsBase
    {
        public override string EditorTitle => "Strm Extract";

        //public override string EditorDescription => "Strm Extract Description";

        [Description("Max Concurrent Count must be between 1 to 10. Default is 1.")]
        [DisplayName("Max Concurrent Count")]
        [MediaBrowser.Model.Attributes.Required]
        public int MaxConcurrentCount { get; set; } = 1;

        protected override void Validate(ValidationContext context)
        {
            if (MaxConcurrentCount<1 || MaxConcurrentCount>10)
            {
                context.AddValidationError(nameof(MaxConcurrentCount), "Max Concurrent Count must be between 1 to 10.");
            }
        }

        [Description("Extract media info of Strm only. Default is True.")]
        [DisplayName("Strm Only")]
        [MediaBrowser.Model.Attributes.Required]
        public bool StrmOnly { get; set; } = true;

        [Description("Include media extras to extract. Default is False.")]
        [DisplayName("Include Extra")]
        [MediaBrowser.Model.Attributes.Required]
        public bool IncludeExtra { get; set; } = false;

        [Description("On-demand and real-time (exclusive to Strm) extract movies and series added to favorites. Default is False.")]
        [DisplayName("Catch-up Mode (Experimental)")]
        [MediaBrowser.Model.Attributes.Required]
        public bool CatchupMode { get; set; } = false;
    }
}
