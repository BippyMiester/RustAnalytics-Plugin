import requests
import re
import os
import zipfile
import hashlib

# Get commit messages since last release
def get_commit_messages_since_last_release(repo, last_release_tag, token):
    url = f"https://api.github.com/repos/{repo}/commits"
    headers = {'Authorization': f'token {token}'}
    params = {'sha': 'main', 'since': last_release_tag}  # Adjust 'sha' if using a different branch
    response = requests.get(url, headers=headers, params=params)

    if response.status_code == 200:
        commits = response.json()
        return [commit['commit']['message'] for commit in commits]
    else:
        print(f"Failed to fetch commits: {response.status_code}")
        return []

# Get MD5 hash of file
def calculate_md5(file_path):
    with open(file_path, 'rb') as file:
        return hashlib.md5(file.read()).hexdigest()

# Get SHA-1 hash of file
def calculate_sha1(file_path):
    with open(file_path, 'rb') as file:
        return hashlib.sha1(file.read()).hexdigest()

# Get SHA-256 hash of file
def calculate_sha256(file_path):
    with open(file_path, 'rb') as file:
        return hashlib.sha256(file.read()).hexdigest()

# Function to replace URL in RustAnalytics.cs
def replace_url():
    with open('RustAnalytics.cs', 'r') as file:
        filedata = file.read()

    # Replace the target string
    filedata = filedata.replace('http://localhost:8000', 'https://rustanalytics.com')

    # Write the file out again
    with open('RustAnalytics.cs', 'w') as file:
        file.write(filedata)

# Function to replace default local dev environment api key in RustAnalytics.cs
def replace_API_key():
    with open('RustAnalytics.cs', 'r') as file:
        filedata = file.read()

    # Replace the target string
    filedata = filedata.replace('7e0c91ce-c7c1-3304-8d40-9eab41cf29f6', 'INSERT_API_KEY_HERE')

    # Write the file out again
    with open('RustAnalytics.cs', 'w') as file:
        file.write(filedata)

        
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

# Function to create a ZIP file with specific files
def bundle_files(files):
    with zipfile.ZipFile('RustAnalytics.zip', 'w') as zipf:
        for file in files:
            zipf.write(file)

# Function to upload a release asset
def upload_release_asset(upload_url, file_path, token):
    headers = {'Authorization': f'token {token}', 'Content-Type': 'application/octet-stream'}
    params = {'name': os.path.basename(file_path)}
    with open(file_path, 'rb') as file:
        response = requests.post(upload_url, headers=headers, params=params, data=file)
        return response.status_code == 201

# Function to create a new release
def create_release(repo, version, token, md5_checksum, sha1_checksum, sha256_checksum, commit_messages):
    commit_messages_str = '\n'.join(f"- {msg}" for msg in commit_messages)
    url = f"https://api.github.com/repos/{repo}/releases"
    headers = {'Authorization': f'token {token}'}
    body = (f'Release of version {version}\n\n'
            f'\n\n'
            f'Commits since last release:\n{commit_messages_str}'
            f'\n\n'
            f'Checksums of RustAnalytics.cs\n'
            f'MD5: {md5_checksum}\n'
            f'SHA-1: {sha1_checksum}\n'
            f'SHA-256: {sha256_checksum}')
    data = {
        'tag_name': version,
        'name': version,
        'body': body,
        'draft': False,
        'prerelease': False
    }
    response = requests.post(url, json=data, headers=headers)
    if response.status_code == 201:
        return True, response.json()['upload_url']
    else:
        print(f"Failed to create release: {response.status_code}")
        print(f"Response: {response.json()}")
        return False, None

# Main execution
def main():
    repo = 'BippyMiester/RustAnalytics-Plugin'  # Replace with your repository
    token = os.environ['GITHUB_TOKEN']  # Ensure GITHUB_TOKEN is set in your secrets
    files_to_bundle = ['RustAnalytics.cs', 'README.md', 'LICENSE.md', 'RustAnalyticsPlaytimeTracker.cs']
    md5_checksum = calculate_md5('RustAnalytics.cs')
    sha1_checksum = calculate_sha1('RustAnalytics.cs')
    sha256_checksum = calculate_sha256('RustAnalytics.cs')
    commit_messages = get_commit_messages_since_last_release(repo, latest_release, token)

    replace_url() # Call the new function to replace the URL
    replace_API_key()

    current_version = extract_version()
    latest_release = get_latest_release(repo)
    print(f"Current Version: {current_version}")
    print(f"Latest Release: {latest_release}")

    # Adjusted logic for release creation and asset upload
    if latest_release is None or (current_version and current_version > latest_release):
        bundle_files(files_to_bundle)
        success, upload_url = create_release(repo, current_version, token, md5_checksum, sha1_checksum, sha256_checksum, commit_messages)
        if success:
            upload_url = upload_url.split('{')[0]
            if upload_release_asset(upload_url, 'RustAnalytics.zip', token):
                print("Release and assets uploaded successfully")
            else:
                print("Failed to upload assets")
        else:
            print("Failed to create release")


if __name__ == "__main__":
    main()