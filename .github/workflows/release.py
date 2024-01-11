import requests
import re
import os

# Function to extract version from RustAnalytics.cs
def extract_version():
    with open('RustAnalytics.cs', 'r') as file:
        for line in file:
            if '_PluginVersion' in line:
                version = re.search(r'\"([\d.]+)\"', line)
                if version:
                    return version.group(1)
    return None

# Function to get the latest release version from GitHub API
def get_latest_release(repo):
    url = f"https://api.github.com/repos/{repo}/releases/latest"
    response = requests.get(url)
    if response.status_code == 200:
        return response.json()['tag_name']
    return None

# Function to create a new release
def create_release(repo, version, token):
    url = f"https://api.github.com/repos/{repo}/releases"
    headers = {'Authorization': f'token {token}'}
    data = {
        'tag_name': version,
        'name': version,
        'body': f'Release of version {version}',
        'draft': False,
        'prerelease': False
    }
    response = requests.post(url, json=data, headers=headers)
    return response.status_code == 201

# Main execution
def main():
    repo = 'BippyMiester/RustAnalytics-Plugin'  # Replace with your repository
    token = os.environ['GITHUB_TOKEN']  # Ensure GITHUB_TOKEN is set in your secrets

    current_version = extract_version()
    latest_release = get_latest_release(repo)
    print(f"Current Version: {current_version}")
    print(f"Latest Release: {latest_release}")

    if current_version and latest_release and current_version > latest_release:
        if create_release(repo, current_version, token):
            print("Release created successfully")
        else:
            print("Failed to create release")
    else:
        print("No new release needed")

if __name__ == "__main__":
    main()