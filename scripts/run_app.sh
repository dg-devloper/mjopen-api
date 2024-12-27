#!/bin/bash

# Get the directory where the script is located
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Application name
APP_NAME="Midjourney.API"

# Check if the application file exists
if [ ! -f "$DIR/$APP_NAME" ]; then
  echo "Error: Application file $DIR/$APP_NAME does not exist."
  exit 1
fi

# Grant execute permission to the application
chmod +x "$DIR/$APP_NAME"

# Execute the application
"$DIR/$APP_NAME"

# Check the application's exit status
if [ $? -ne 0 ]; then
  echo "Error: Application execution failed."
  exit 1
else
  echo "Application executed successfully."
fi