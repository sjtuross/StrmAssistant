namespace StrmAssistant.Options.UIBaseClasses.Store
{
    using System;

    using Emby.Web.GenericEdit;

    public class FileSavedEventArgs : EventArgs
    {
        /// <summary>Initializes a new instance of the <see cref="FileSavedEventArgs"/> class.</summary>
        /// <param name="options">The options.</param>
        public FileSavedEventArgs(EditableOptionsBase options)
        {
            this.Options = options;
        }

        /// <summary>Gets the options.</summary>
        /// <value>The options.</value>
        public EditableOptionsBase Options { get; }
    }
}
