using AalgTrips.Models;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace AalgTrips.TagHelpers
{
    [HtmlTargetElement("img", Attributes = "photo, type")]
    public class PhotoTagHelper : TagHelper
    {
        public Photo Photo { get; set; }

        public ImageType Type { get; set; }

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            string thumbnail = Photo.GetThumbnailLink((int)Type, out int height);

            if (string.IsNullOrEmpty(thumbnail))
            {
                output.SuppressOutput();
                return;
            }

            output.Attributes.SetAttribute("width", (int)Type);
            output.Attributes.SetAttribute("height", height);
            output.Attributes.SetAttribute("alt", Photo.DisplayName);
            output.Attributes.SetAttribute("src", thumbnail);

            if (Type != ImageType.Full)
            {
                string thumb = Photo.GetThumbnailLink((int)ImageType.Thumbnail, out int thumbHeight);
                string cover = Photo.GetThumbnailLink((int)ImageType.Cover, out int coverHeight);
                output.Attributes.SetAttribute("srcset", $"{thumb} 1x, {cover} 2x");

                // Album grids can hold dozens of photos; without this the browser
                // requests every thumbnail — including ones far below the fold —
                // the moment the page loads, and each one is proxied through the
                // authenticated media endpoint. Defer off-screen thumbnails so the
                // visible grid renders fast and the rest stream in on scroll. The
                // width/height set above keep the layout stable as they arrive.
                output.Attributes.SetAttribute("loading", "lazy");
                output.Attributes.SetAttribute("decoding", "async");
            }
        }
    }
}