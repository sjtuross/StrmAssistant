namespace StrmAssistant.Options.UIBaseClasses.Store
{
    using System;

    using Emby.Web.GenericEdit;

    public class FileSavingEventArgs : EventArgs
    {
        /// <summary>Initializes a new instance of the <see cref="FileSavingEventArgs"/> class.</summary>
        /// <param name="options">The options.</param>
        public FileSavingEventArgs(EditableOptionsBase options)
        {
            this.Options = options;
        }

        /// <summary>Gets the options.</summary>
        /// <value>The options.</value>
        public EditableOptionsBase Options { get; }

        /// <summary>Gets or sets a value indicating whether saving should be cancelled.</summary>
        /// <value><c>true</c> to cancel saving; otherwise, <c>false</c>.</value>
        public bool Cancel { get; set; }
    }
}
