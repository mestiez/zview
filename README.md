# zview

Image viewer for X11 heavily inspired by [NoBS Image Viewer](https://ropemikad.itch.io/nobs-image-viewer).

Supports `tga`, `tiff`, `png`, `jpg`, `webp`, `gif`, and `qoi`.

## Dependencies

- .NET 10
- SDL3
- ImageSharp

## Building & installing

For filesize concerns, it's best to have the .NET 10 runtime installed. Run the following as a user to build and install
to `~/.local/bin`.

```
dotnet publish -p:PublishProfile=framework-dependent
```

## Usage

`zview [filename or folder path]`

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
