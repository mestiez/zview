# zview

Image viewer for X11 heavily inspired by [NoBS Image Viewer](https://ropemikad.itch.io/nobs-image-viewer).

Supports `tga`, `tiff`, `png`, `jpg`, `webp`, `gif`, and `qoi`.

## Dependencies

- .NET 10
- libSDL3

## Building & installing

Ensure SDL3 and .NET 10 SDK are installed.

Run the following as a user to build and install
to `~/.local/bin`. You can provide an alternative output directory using the `--output` option.

```
dotnet publish -p:PublishProfile=framework-dependent
```

Or use the [prebuilt binaries](/mestiez/zview/releases) if you're on amd64.

## Usage

    zview [options] [path]

	Arguments:
    path              Path to the image or directory (optional).

    Options:
    -v, --version     Display version information.
    -h, --help        Show help and usage information.

You can also drag the file into the window.
Touchscreen pan and pinch zoom are supported.

|         Input | Action                  |
|--------------:|:------------------------|
|         `LMB` | Pan                     |
|           `R` | Rotate                  |
|           `V` | Flip (vertical)         |
|           `H` | Flip (horizontal)       |
|        `Home` | Reset camera            |
|           `.` | Auto fit image          |
|           `W` | Auto size window        |
|      `Ctrl+W` | Toggle auto size window |
|           `B` | Toggle background       |
|           `F` | Toggle linear filtering |
|      `Ctrl+V` | Load from clipboard     |
|          `->` | Next in directory       |
|          `<-` | Previous in directory   |
|          `F5` | Reload image            |
| `Q`, `Escape` | Quit                    |

## Known issues and limitations

- All images are fully loaded into memory before being displayed. For large and/or animated images, this can become a
  problem. 
- HDR images aren't rendered correctly
- The directory queue is reloaded every time an arrow key is pressed (should probably be cached until F5 is pressed)
