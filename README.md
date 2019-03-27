# Custom Vision Service Trainer
A small application that allows to automatically upload and  tag images for Custom Vision Service.

| Parameter | Short Version | Long Version |
| ------ | ------ | ------ |
| Specify the training key | **t** | trainingkey |
| Specify the project to upload images for | **p** | project |
| Specify the directory that contains the images to be used for training | **f** | folder |
| Specify the width of the images | **w** | width |
| Specify the height of the images | **h** | height |
| If no folder is specified, delete images and tags and exits, otherwise delete images and tags and upload the images | **d** | delete |

### Installing the .NET CLI Tool
In order to install the .NET CLI Tool version of this application, you need to simly type inside the command prompt the following command
```console
dotnet tool install -g CustomVisionTrainer
```
Or if you want to specify the version you can do that by doing
```console
dotnet tool install -g CustomVisionTrainer --version 1.0.0
```
If installation is successful, a message is displayed showing the command used to call the tool and the version installed, similar to the following example:

```
You can invoke the tool using the following command: cvtrainer
Tool 'CustomVisionTrainer' (version '1.0.0') was successfully installed.
```
### Updating the .NET CLI Tool
After a new release of this application , you can update the CLI Tool like this
```console
dotnet tool update -g CustomVisionTrainer
```
### Uninstalling the .NET CLI Tool
In case you don't want this tool installed on your system any longer, you can remove it by doing
```console
dotnet tool uninstall -g CustomVisionTrainer
```
=======
